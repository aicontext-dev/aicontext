namespace AIContext.Tests;

// The example of the library README (src/AIContext/README.md) as code: DocumentationExamplesTests compiles it with
// the tests, runs it against temporary projects, and checks that the README shows exactly the lines between the
// markers. A change of the API that the example uses therefore stops the build, and a change of the example that
// is not made in both places fails a test. The example is the body of a console program, so it returns an exit code:
// 0 completed, 1 refused before anything was written, 2 Apply failed.
internal static class LibraryReadmeSample
{
    public static int Run(string projectPath)
    {
        // <sample>
        var plan = ExportService.Plan(new ExportRequest(projectPath, Target: "all"));
        if (plan.HasErrors)
        {
            // Refused before anything was written: an invalid context, an overwrite conflict, a path that leaves the
            // project, a destination that is blocked on disk.
            foreach (var d in plan.Diagnostics.Where(d => d.IsError))
                Console.Error.WriteLine($"{d.Code} {d.Location}: {d.Message}");
            return 1;
        }

        var result = ExportService.Apply(plan);
        foreach (var file in result.Written)
            Console.WriteLine($"{file.Status}: {file.Path}");

        if (result.HasErrors)
        {
            // Apply checks the plan against the disk again before it writes: a plan that went stale (AIC4103), a path that
            // now leaves the project (AIC4102, AIC4104) or a file that cannot be written (AIC4105, AIC4106) writes nothing.
            // A write that fails midway stops there: result.Written is what was done and stays on disk, nothing is rolled
            // back, and planning again continues from the files as they are.
            foreach (var d in result.Diagnostics.Where(d => d.IsError))
                Console.Error.WriteLine($"{d.Code} {d.Location}: {d.Message}");
            Console.Error.WriteLine($"{result.Written.Count} of {plan.ChangedCount} planned change(s) were written.");
            return 2;
        }

        Console.WriteLine($"{result.Changed} changed, {result.Unchanged} unchanged.");
        return 0;
        // </sample>
    }
}
