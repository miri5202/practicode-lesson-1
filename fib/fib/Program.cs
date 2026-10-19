using System.CommandLine;

// ============================================================
//  הגדרות כלליות
// ============================================================

// מילון שפות התכנות הנתמכות -> סיומות הקבצים של כל שפה
var languages = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
{
    { "csharp",     new[] { ".cs" } },
    { "java",       new[] { ".java" } },
    { "python",     new[] { ".py" } },
    { "javascript", new[] { ".js", ".jsx" } },
    { "typescript", new[] { ".ts", ".tsx" } },
    { "html",       new[] { ".html", ".htm" } },
    { "css",        new[] { ".css", ".scss" } },
    { "c",          new[] { ".c", ".h" } },
    { "cpp",        new[] { ".cpp", ".hpp" } },
    { "sql",        new[] { ".sql" } },
    { "json",       new[] { ".json" } },
    { "xml",        new[] { ".xml" } }
};

// תיקיות שאין לכלול ב-bundle
var excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "bin", "obj", "debug", "release", "node_modules", "packages", ".vs", ".git", ".idea", ".vscode"
};

// ============================================================
//  הגדרת האפשרויות (Options) של פקודת bundle
// ============================================================

// language - רשימת שפות (חובה)
var languageOption = new Option<string[]>(
    aliases: new[] { "--language", "-l" },
    description: "List of programming languages to include (or 'all'). Supported: "
                 + string.Join(", ", languages.Keys))
{
    IsRequired = true,
    AllowMultipleArgumentsPerToken = true // מאפשר: -l csharp java python
};
languageOption.AddValidator(result =>
{
    var values = result.GetValueOrDefault<string[]>() ?? Array.Empty<string>();
    if (values.Length == 0)
    {
        result.ErrorMessage = "Error: you must specify at least one language (or 'all').";
        return;
    }
    var invalid = values.Where(v => !v.Equals("all", StringComparison.OrdinalIgnoreCase)
                                    && !languages.ContainsKey(v)).ToList();
    if (invalid.Any())
    {
        result.ErrorMessage = $"Error: unknown language(s): {string.Join(", ", invalid)}. "
                            + $"Supported languages: {string.Join(", ", languages.Keys)}, all";
    }
});

// output - שם/ניתוב קובץ הפלט
var outputOption = new Option<FileInfo>(
    aliases: new[] { "--output", "-o" },
    description: "File name or full path of the bundle file",
    getDefaultValue: () => new FileInfo("bundle.txt"));

// note - האם לרשום את מקור הקוד כהערה
var noteOption = new Option<bool>(
    aliases: new[] { "--note", "-n" },
    description: "Write the source file name and relative path as a comment");

// sort - סדר העתקת הקבצים
var sortOption = new Option<string>(
    aliases: new[] { "--sort", "-s" },
    description: "Sort files by 'name' (alphabetical, default) or by 'type' (code language)",
    getDefaultValue: () => "name");
sortOption.FromAmong("name", "type"); // ולידציה - רק אחד משני הערכים

// remove-empty-lines - מחיקת שורות ריקות
var removeEmptyLinesOption = new Option<bool>(
    aliases: new[] { "--remove-empty-lines", "-r" },
    description: "Remove empty lines from the source code");

// author - שם יוצר הקובץ
var authorOption = new Option<string>(
    aliases: new[] { "--author", "-a" },
    description: "Name of the bundle author");
authorOption.AddValidator(result =>
{
    var value = result.GetValueOrDefault<string>();
    if (value != null && string.IsNullOrWhiteSpace(value))
        result.ErrorMessage = "Error: author name cannot be empty.";
});

// ============================================================
//  פקודת bundle
// ============================================================

var bundleCommand = new Command("bundle", "Bundle code files into a single file");
bundleCommand.AddOption(languageOption);
bundleCommand.AddOption(outputOption);
bundleCommand.AddOption(noteOption);
bundleCommand.AddOption(sortOption);
bundleCommand.AddOption(removeEmptyLinesOption);
bundleCommand.AddOption(authorOption);

bundleCommand.SetHandler((language, output, note, sort, removeEmptyLines, author) =>
{
    try
    {
        string sourceDir = Directory.GetCurrentDirectory();

        // 1. קביעת הסיומות הרצויות
        bool includeAll = language.Any(l => l.Equals("all", StringComparison.OrdinalIgnoreCase));
        var extensions = includeAll
            ? languages.Values.SelectMany(e => e).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : language.SelectMany(l => languages[l]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2. איסוף הקבצים מהתיקיה הנוכחית ותתי-התיקיות
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !IsInExcludedFolder(Path.GetRelativePath(sourceDir, f), excludedFolders))
            .Where(f => !Path.GetFullPath(f).Equals(output.FullName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("No code files were found for the selected language(s).");
            return;
        }

        // 3. מיון
        files = sort.Equals("type", StringComparison.OrdinalIgnoreCase)
            ? files.OrderBy(f => Path.GetExtension(f)).ThenBy(f => Path.GetFileName(f)).ToList()
            : files.OrderBy(f => Path.GetFileName(f)).ToList();

        // 4. כתיבה לקובץ ה-bundle
        using var writer = new StreamWriter(output.FullName);

        if (!string.IsNullOrWhiteSpace(author))
            writer.WriteLine($"// Author: {author}");

        foreach (var file in files)
        {
            if (note)
                writer.WriteLine($"// Source: {Path.GetFileName(file)} | Path: {Path.GetRelativePath(sourceDir, file)}");

            var lines = File.ReadAllLines(file);
            if (removeEmptyLines)
                lines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

            foreach (var line in lines)
                writer.WriteLine(line);

            writer.WriteLine(); // שורה מפרידה בין קבצים
        }

        Console.WriteLine($"Bundle created successfully: {output.FullName} ({files.Count} files)");
    }
    catch (DirectoryNotFoundException)
    {
        Console.WriteLine("Error: the output path is invalid - the folder does not exist.");
    }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine("Error: no permission to write to the output path.");
    }
    catch (IOException ex)
    {
        Console.WriteLine($"Error: could not save the bundle file. {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected error: {ex.Message}");
    }
}, languageOption, outputOption, noteOption, sortOption, removeEmptyLinesOption, authorOption);

// ============================================================
//  פקודת create-rsp
// ============================================================

var createRspCommand = new Command("create-rsp", "Create a response file with a ready bundle command");

createRspCommand.SetHandler(() =>
{
    try
    {
        // language - חובה, עם ולידציה
        string languageAnswer;
        while (true)
        {
            Console.Write($"Enter languages separated by spaces ({string.Join(", ", languages.Keys)}, or 'all'): ");
            languageAnswer = (Console.ReadLine() ?? "").Trim();
            var parts = languageAnswer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                Console.WriteLine("Error: language is required.");
                continue;
            }
            var invalid = parts.Where(p => !p.Equals("all", StringComparison.OrdinalIgnoreCase)
                                           && !languages.ContainsKey(p)).ToList();
            if (invalid.Any())
            {
                Console.WriteLine($"Error: unknown language(s): {string.Join(", ", invalid)}");
                continue;
            }
            languageAnswer = string.Join(" ", parts);
            break;
        }

        // output
        Console.Write("Enter output file name or full path (Enter for 'bundle.txt'): ");
        string outputAnswer = (Console.ReadLine() ?? "").Trim().Trim('"');
        if (outputAnswer == "") outputAnswer = "bundle.txt";

        // note
        bool noteAnswer = AskYesNo("Add source file name and path as a comment? (y/n): ");

        // sort
        string sortAnswer;
        while (true)
        {
            Console.Write("Sort by 'name' or 'type' (Enter for 'name'): ");
            sortAnswer = (Console.ReadLine() ?? "").Trim().ToLower();
            if (sortAnswer == "") sortAnswer = "name";
            if (sortAnswer == "name" || sortAnswer == "type") break;
            Console.WriteLine("Error: please enter 'name' or 'type'.");
        }

        // remove-empty-lines
        bool removeAnswer = AskYesNo("Remove empty lines? (y/n): ");

        // author
        Console.Write("Enter author name (Enter to skip): ");
        string authorAnswer = (Console.ReadLine() ?? "").Trim();

        // שם קובץ התגובה
        string rspName;
        while (true)
        {
            Console.Write("Enter response file name (without extension): ");
            rspName = (Console.ReadLine() ?? "").Trim();
            if (rspName != "" && rspName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) break;
            Console.WriteLine("Error: invalid file name.");
        }

        // בניית הפקודה המלאה - כל אפשרות בשורה נפרדת
        var lines = new List<string>
        {
            "bundle",
            $"--language {languageAnswer}",
            $"--output \"{outputAnswer}\"",
            $"--sort {sortAnswer}"
        };
        if (noteAnswer) lines.Add("--note");
        if (removeAnswer) lines.Add("--remove-empty-lines");
        if (authorAnswer != "") lines.Add($"--author \"{authorAnswer}\"");

        string rspFile = rspName + ".rsp";
        File.WriteAllLines(rspFile, lines);

        Console.WriteLine($"Response file created: {Path.GetFullPath(rspFile)}");
        Console.WriteLine($"Run it with:  fib @{rspFile}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: could not create the response file. {ex.Message}");
    }
});

// ============================================================
//  פקודת השורש
// ============================================================

var rootCommand = new RootCommand("Root command for file bundler CLI");
rootCommand.AddCommand(bundleCommand);
rootCommand.AddCommand(createRspCommand);

return await rootCommand.InvokeAsync(args);

// ============================================================
//  פונקציות עזר
// ============================================================

// בודקת אם הקובץ נמצא בתוך תיקיה שאסור לכלול (bin, obj, debug...)
static bool IsInExcludedFolder(string relativePath, HashSet<string> excluded)
{
    var folders = Path.GetDirectoryName(relativePath)?
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? Array.Empty<string>();
    return folders.Any(excluded.Contains);
}

// שאלת כן/לא עם ולידציה
static bool AskYesNo(string question)
{
    while (true)
    {
        Console.Write(question);
        var answer = (Console.ReadLine() ?? "").Trim().ToLower();
        if (answer == "y" || answer == "yes") return true;
        if (answer == "n" || answer == "no" || answer == "") return false;
        Console.WriteLine("Error: please answer 'y' or 'n'.");
    }
}
