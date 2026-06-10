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

static HKHealthStore *_healthStore = nil;
static std::atomic<int> _authResult{-1};
static std::atomic<int> _stepsResult{-1};
static std::atomic<int> _runSecondsResult{-1};
// Remote-config-driven anti-cheat toggle. Defaults to off so manually-entered
// Health data still counts (reviewer-friendly). C# flips this when the server
// config or the local cache says to. Atomic so the toggle is safe to race
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

// Apple only exposes write-side authorization status — read-side is opaque
// by design (so apps can't fingerprint users by probing which HK types are
// authorized). The status returned here is "did the user respond to our
// prompt" not "can we actually read steps". The query call below will just
// return 0 if read access was declined.
int _HealthKitAuthStatus() {
    if (![HKHealthStore isHealthDataAvailable]) return 1;  // Denied
    HKQuantityType *stepType = [HKQuantityType quantityTypeForIdentifier:HKQuantityTypeIdentifierStepCount];
    HKAuthorizationStatus status = [HK() authorizationStatusForType:stepType];
    return (int)status;
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
        // success == YES means "user responded" (either Allow or Deny). Map to
        // our 3-state status; on error fall through to Denied so the game UX
        // doesn't hang waiting for a positive result that'll never come.
        HKAuthorizationStatus status = [HK() authorizationStatusForType:stepType];
        _authResult.store(success ? (int)status : 1);
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
    // HKStatisticsQuery with CumulativeSum is the standard pattern for
    // "total steps in time window". A SampleQuery would return raw samples
    // which we'd need to sum manually + dedupe across HK sources (phone +
    // watch can both write steps and HK reconciles them inside Statistics).
    // NOTE: when _filterManualEntries flips to true (v1.1+), this query
    // needs to switch to HKSampleQuery + iterate + check
    // HKMetadataKeyWasUserEntered. That refactor is intentionally deferred
    // — for v1.0 the filter only protects the new workout pathway.
    HKStatisticsQuery *query = [[HKStatisticsQuery alloc]
        initWithQuantityType:stepType
     quantitySamplePredicate:predicate
                     options:HKStatisticsOptionCumulativeSum
           completionHandler:^(HKStatisticsQuery * _Nonnull q,
                               HKStatistics * _Nullable result,
                               NSError * _Nullable error) {
        int steps = 0;
        if (result != nil) {
            HKQuantity *sum = result.sumQuantity;
            if (sum != nil) {
                steps = (int)[sum doubleValueForUnit:[HKUnit countUnit]];
            }
        }
        _stepsResult.store(steps);
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
// Filter: when _filterManualEntries is true, workouts with
// HKMetadataKeyWasUserEntered = YES are skipped. That's Apple's canonical
// "this sample was typed in via the Health app" flag.
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
        bool filter = _filterManualEntries.load();
        NSTimeInterval total = 0;
        for (HKWorkout *workout in results) {
            if (filter) {
                NSNumber *wasUserEntered = workout.metadata[HKMetadataKeyWasUserEntered];
                if ([wasUserEntered boolValue]) continue;
            }
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
// reviewer-friendly), non-0 to exclude manually-entered Health samples.
// Idempotent and thread-safe — the next query will pick up the new value.
void _HealthKitSetFilterManualEntries(int filter) {
    _filterManualEntries.store(filter != 0);
}

}  // extern "C"
