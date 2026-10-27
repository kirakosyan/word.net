using System;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet;

namespace WordNet.Examples;

internal static class P3Recipes
{
    internal static void Run(string directory, string templates, CancellationToken cancellationToken)
    {
        var customers = new[] {
            new { Name = "Ada", Lines = new[] { new { Description = "Consulting", Amount = 20m }, new { Description = "Support", Amount = 10m } } },
            new { Name = "Bea", Lines = new[] { new { Description = "Delivery", Amount = 15m } } },
        };
        var data = new { Customers = customers.Select(c => new { c.Name, c.Lines, Total = c.Lines.Sum(line => line.Amount) }).ToArray() };
        using var input = File.OpenRead(Path.Combine(templates, "p3-statement.docx"));
        WordDocument statement = WordDocument.Load(input, DocumentFormat.Docx, out _, AutomationRecipes.Options(), cancellationToken);
        statement.Merge(data, cancellationToken: cancellationToken);
        AutomationRecipes.Export(statement, directory, "p3-statement", Console.Out, cancellationToken);
    }
}
