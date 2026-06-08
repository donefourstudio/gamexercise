// HealthKit native bridge for Gamexercise.
//
// Exposes 4 C-callable functions that the Unity C# side talks to via
// DllImport("__Internal"). The async HealthKit completion handlers run on
// a background queue, so each call writes its result into a static atomic
// int and the C# side polls every frame from HealthKitTicker.Update.
//
// API:
//   _HealthKitIsAvailable()       -> 1 if HealthKit available on this device
//   _HealthKitAuthStatus()        -> 0 NotDetermined / 1 Denied / 2 Authorized
//   _HealthKitRequestAuth()       -> kicks off async; poll _HealthKitGetAuthResult
//   _HealthKitGetAuthResult()     -> -1 pending, 0/1/2 once completed
//   _HealthKitQueryTodaySteps()   -> kicks off async; poll _HealthKitGetStepsResult
//   _HealthKitGetStepsResult()    -> -1 pending, >=0 step count once completed

#import <Foundation/Foundation.h>
#import <HealthKit/HealthKit.h>
#include <atomic>

static HKHealthStore *_healthStore = nil;
static std::atomic<int> _authResult{-1};
static std::atomic<int> _stepsResult{-1};

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
    NSSet *readSet = [NSSet setWithObject:stepType];
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

}  // extern "C"
