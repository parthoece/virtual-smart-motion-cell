namespace VirtualSmartMotionCell.Contracts;

public enum MachineMode { Offline, Manual, Automatic, Maintenance, Recovery }
public enum ExecutionState { Stopped, Initializing, Homing, Ready, Starting, Running, Pausing, Paused, Stopping, Aborting, Recovering, RecoveryRequired, Faulted }
public enum ProductionStep { None, WaitForPart, MoveToPick, Pick, MoveToInspect, Inspect, MoveToPlace, Place, ReturnHome, Complete }
public enum AxisMotionState { Disabled, Standstill, Homing, Moving, Holding, Faulted }
public enum AlarmLifecycle { ActiveUnacknowledged, ActiveAcknowledged, ClearedUnacknowledged, Historical }
public enum AlarmSeverity { Information, Warning, Error, Critical }
public enum CommandStatus { Accepted, Rejected, Failed }
public enum PartQuality { Unknown, Good, Reject }
public enum ProductionOrderStatus { Queued, Active, Paused, Completed, Cancelled }
public enum RecipeLifecycle { Draft, Approved, Active, Retired }
public enum RecoveryAction { DiscardAndReset, RehomeAndReset, ResumeSimulation }
public enum IntegrationHealth { Unknown, Healthy, Degraded, Offline }
public enum MotionFault { None, DriveFaultX, DriveFaultY, FollowingError, FrozenAxisX, FrozenAxisY }
