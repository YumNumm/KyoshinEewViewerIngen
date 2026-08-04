namespace ReplayGenerator.Models;

public enum TriggerType
{
	ShakeDetection,
	Earthquake,
	Eew,
}

public enum SessionStatus
{
	Tracking,
	Waiting,
	Generating,
	Done,
}
