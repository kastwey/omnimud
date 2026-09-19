// The localized Strings class has a process-wide Culture, and WinForms tests create real
// windows: running test classes in parallel would make them step on each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
