// HealthKit native bridge for Gamexercise.
//
// Exposes C-callable functions that the Unity C# side talks to via
// DllImport("__Internal"). The async HealthKit completion handlers run on
// a background queue, so each call writes its result into a static atomic
// int and the C# side polls every frame from HealthKitTicker.Update.
//
// API:
//   _HealthKitIsAvailable()              -> 1 if HealthKit available on this device
//   _HealthKitAuthStatus()               -> 0 NotDetermined / 1 Denied / 2 Authorized
//   _HealthKitRequestAuth()              -> kicks off async; poll _HealthKitGetAuthResult
//   _HealthKitGetAuthResult()            -> -1 pending, 0/1/2 once completed
//   _HealthKitQueryTodaySteps()          -> kicks off async; poll _HealthKitGetStepsResult
//   _HealthKitGetStepsResult()           -> -1 pending, >=0 step count once completed
//   _HealthKitQueryTodayRunSeconds()     -> kicks off async; poll _HealthKitGetRunSecondsResult
//   _HealthKitGetRunSecondsResult()      -> -1 pending, >=0 running-workout total seconds today
//   _HealthKitSetFilterManualEntries(f)  -> 0 = include manual entries, non-0 = exclude
//                                           (applied to workouts in v1.0; step filter is a
//                                            v1.1 TODO — see RemoteConfig.cs for the toggle.)

#import <Foundation/Foundation.h>
#import <HealthKit/HealthKit.h>
#include <atomic>

// Persisted-auth user-defaults key. Apple's authorizationStatusForType API
// returns sharingDenied for any READ-ONLY HealthKit request even after the
// user granted access — this is a deliberate privacy feature so apps can't
// probe what users authorized. Without persisting our own resolution state,
// our HealthKit gate stays "denied" forever after the user approves the
// modal. We write the post-modal outcome here (2 = Authorized after the
// user responded YES to the modal, 1 = Denied if we got an error or the
// device doesn't support HealthKit), and trust this on subsequent launches.
static NSString *const kHKAuthResolvedKey = @"GamexerciseHKAuthResolved";

static HKHealthStore *_healthStore = nil;
static std::atomic<int> _authResult{-1};
static std::atomic<int> _stepsResult{-1};
static std::atomic<int> _runSecondsResult{-1};
// Remote-config-driven anti-cheat toggle for STEPS ONLY. Defaults to off
// so manually-entered step data still counts (reviewer-friendly). When
// flipped on, _HealthKitQueryTodaySteps skips HKQuantitySamples flagged
// HKMetadataKeyWasUserEntered. Workouts are unaffected and always
// accepted regardless of this flag — see the v1.0 anti-cheat design in
// project_anticheat_toggle.md. Atomic so the toggle is safe to race
// against an in-flight query callback.
static std::atomic<bool> _filterManualEntries{false};

static HKHealthStore* HK() {
    if (_healthStore == nil) _healthStore = [[HKHealthStore alloc] init];
    return _healthStore;
}

extern "C" {

int _HealthKitIsAvailable() {
    return [HKHealthStore isHealthDataAvailable] ? 1 : 0;
}

// Apple only exposes write-side authorization status — for read-only
// requests, authorizationStatusForType always returns sharingDenied
// (privacy feature, prevents apps from probing user-granted permissions).
// Our workaround: persist the post-modal user response in NSUserDefaults
// and trust THAT on subsequent calls instead of asking Apple. Before the
// first auth prompt, we return NotDetermined (0) so the gate fires.
int _HealthKitAuthStatus() {
    if (![HKHealthStore isHealthDataAvailable]) return 1;  // Denied
    NSNumber *resolved = [[NSUserDefaults standardUserDefaults] objectForKey:kHKAuthResolvedKey];
    if (resolved != nil) return [resolved intValue];
    return 0;  // NotDetermined — never asked
}

void _HealthKitRequestAuth() {
    _authResult.store(-1);
    if (![HKHealthStore isHealthDataAvailable]) {
        _authResult.store(1);
        return;
    }
    HKQuantityType *stepType = [HKQuantityType quantityTypeForIdentifier:HKQuantityTypeIdentifierStepCount];
    HKObjectType  *workoutType = [HKObjectType workoutType];
    // Single prompt covers both types — iOS surfaces them as separate toggles
    // on the grant sheet. User can deny workouts but allow steps; the run
    // query gracefully returns 0 in that case so walking quests still work.
    NSSet *readSet = [NSSet setWithObjects:stepType, workoutType, nil];
    [HK() requestAuthorizationToShareTypes:nil
                                  readTypes:readSet
                                 completion:^(BOOL success, NSError * _Nullable error) {
        // success == YES means "user responded" (Allow or Deny). For read-
        // only auth, authorizationStatusForType is unreliable (always returns
        // sharingDenied — see comment above _HealthKitAuthStatus). We trust
        // the modal completion: if user responded, assume Authorized and let
        // the query path silently return 0 if they actually denied. The
        // player can always re-grant via iOS Settings → Privacy → Health.
        int finalStatus = success ? 2 : 1;  // 2 = Authorized, 1 = Denied
        [[NSUserDefaults standardUserDefaults] setObject:@(finalStatus) forKey:kHKAuthResolvedKey];
        [[NSUserDefaults standardUserDefaults] synchronize];
        _authResult.store(finalStatus);
    }];
}

int _HealthKitGetAuthResult() {
    return _authResult.load();
}

void _HealthKitQueryTodaySteps() {
    _stepsResult.store(-1);
    if (![HKHealthStore isHealthDataAvailable]) {
        _stepsResult.store(0);
        return;
    }
    HKQuantityType *stepType = [HKQuantityType quantityTypeForIdentifier:HKQuantityTypeIdentifierStepCount];
    NSCalendar *calendar = [NSCalendar currentCalendar];
    NSDate *startOfDay = [calendar startOfDayForDate:[NSDate date]];
    NSDate *now = [NSDate date];
    NSPredicate *predicate = [HKQuery predicateForSamplesWithStartDate:startOfDay
                                                              endDate:now
                                                              options:HKQueryOptionStrictStartDate];
    // HKSampleQuery (not HKStatisticsQuery) so we can inspect per-sample
    // metadata for the manual-entry anti-cheat filter. The cost vs the
    // statistics-aggregation path: we sum samples ourselves and don't get
    // HK's automatic cross-source deduplication (phone + Apple Watch can
    // both record the same minute of walking; HKStatistics merges them).
    //
    // For the launch demographic (single device, no Watch in most cases),
    // double-counting is rare enough to accept as a tradeoff for the
    // anti-cheat protection. Users who do have both devices will see
    // slightly inflated daily totals, which favours them (more coins,
    // streak preserved on borderline days) so the failure mode is benign.
    HKSampleQuery *query = [[HKSampleQuery alloc]
        initWithSampleType:stepType
                 predicate:predicate
                     limit:HKObjectQueryNoLimit
           sortDescriptors:nil
            resultsHandler:^(HKSampleQuery * _Nonnull q,
                             NSArray<__kindof HKSample *> * _Nullable results,
                             NSError * _Nullable error) {
        if (error != nil || results == nil) {
            _stepsResult.store(0);
            return;
        }
        bool filter = _filterManualEntries.load();
        double total = 0;
        for (HKQuantitySample *sample in results) {
            if (filter) {
                NSNumber *wasUserEntered = sample.metadata[HKMetadataKeyWasUserEntered];
                if ([wasUserEntered boolValue]) continue;
            }
            total += [sample.quantity doubleValueForUnit:[HKUnit countUnit]];
        }
        _stepsResult.store((int)total);
    }];
    [HK() executeQuery:query];
}

int _HealthKitGetStepsResult() {
    return _stepsResult.load();
}

// Sums today's running-workout durations. Workouts are HKWorkout objects
// (not HKQuantity samples), so this uses HKSampleQuery rather than the
// cumulative-sum statistics path the step query uses.
//
// Date predicate: workouts whose start time falls in today. A workout that
// straddles midnight will still surface for the day it started — acceptable
// for our quest math; over-credit is bounded to one extra workout per day.
// Type predicate: HKWorkoutActivityTypeRunning only — walking workouts
// already contribute steps via the step query, no double-counting needed.
//
// Manual entries: deliberately NOT filtered. Per the v1.0 anti-cheat
// design, _filterManualEntries protects step counts only — workouts are
// always accepted regardless of source. Rationale: streak fraud + walk-
// quest fraud is more valuable to protect against than run-quest fraud
// (smaller coin payout), and reviewer-testability requires manual
// workouts to count. See project_anticheat_toggle.md.
void _HealthKitQueryTodayRunSeconds() {
    _runSecondsResult.store(-1);
    if (![HKHealthStore isHealthDataAvailable]) {
        _runSecondsResult.store(0);
        return;
    }
    NSCalendar *calendar = [NSCalendar currentCalendar];
    NSDate *startOfDay = [calendar startOfDayForDate:[NSDate date]];
    NSDate *now = [NSDate date];
    NSPredicate *datePredicate = [HKQuery predicateForSamplesWithStartDate:startOfDay
                                                                   endDate:now
                                                                   options:HKQueryOptionStrictStartDate];
    NSPredicate *typePredicate = [HKQuery predicateForWorkoutsWithWorkoutActivityType:HKWorkoutActivityTypeRunning];
    NSPredicate *combined = [NSCompoundPredicate andPredicateWithSubpredicates:@[datePredicate, typePredicate]];

    HKSampleQuery *query = [[HKSampleQuery alloc]
        initWithSampleType:[HKObjectType workoutType]
                 predicate:combined
                     limit:HKObjectQueryNoLimit
           sortDescriptors:nil
            resultsHandler:^(HKSampleQuery * _Nonnull q,
                             NSArray<__kindof HKSample *> * _Nullable results,
                             NSError * _Nullable error) {
        if (error != nil || results == nil) {
            _runSecondsResult.store(0);
            return;
        }
        NSTimeInterval total = 0;
        for (HKWorkout *workout in results) {
            total += workout.duration;
        }
        _runSecondsResult.store((int)total);
    }];
    [HK() executeQuery:query];
}

int _HealthKitGetRunSecondsResult() {
    return _runSecondsResult.load();
}

// Remote-config flip from C# side. Pass 0 to count everything (default,
// reviewer-friendly), non-0 to exclude manually-entered HKQuantity step
// samples. Idempotent and thread-safe — the next query picks up the new
// value. NOTE: workouts are always accepted regardless of this flag (see
// _HealthKitQueryTodayRunSeconds for rationale).
void _HealthKitSetFilterManualEntries(int filter) {
    _filterManualEntries.store(filter != 0);
}

// Manual recovery escape hatch — wipes our persisted post-modal auth
// resolution so the next _HealthKitAuthStatus call returns NotDetermined,
// which re-triggers the HealthKit gate. Driven by the Settings panel's
// "Reconnect HealthKit" button: clear the cache, deep-link to iOS
// Privacy → Health, let OnApplicationFocus re-evaluate the gate on
// return. iOS's own modal-already-shown state is independent and not
// reset by this (we can't touch it), but the request still resolves
// success=YES on the next attempt, so the gate flow completes either
// way.
void _HealthKitResetAuthCache() {
    [[NSUserDefaults standardUserDefaults] removeObjectForKey:kHKAuthResolvedKey];
    [[NSUserDefaults standardUserDefaults] synchronize];
}

}  // extern "C"
