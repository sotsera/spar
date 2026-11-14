// Short names for the two result types most cases use. The analyzer sees through aliases.
global using Spar;
global using SqlTool.CopyJobs;
global using CancelResult = Spar.Result<SqlTool.CopyJobs.CancelError>;
global using PlanResult = Spar.Result<SqlTool.CopyJobs.CopyPlan, SqlTool.CopyJobs.PlanError>;
