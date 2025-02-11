using System.Globalization;
using CsvHelper;
using IniParser;
using IniParser.Model;
using Mono.Cecil;
using Mono.Cecil.Cil;

class DllModifier
{
    public class CsvEntry
    {
        public required string ClassName { get; set; }
        public required string MethodName { get; set; }
        public required string Text { get; set; }
        public required string NewText { get; set; }
    }

    static void ExportLdstrStrings(AssemblyDefinition assembly, string csvFilePath)
    {
        var entries = new List<CsvEntry>();

        foreach (var type in assembly.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldstr)
                    {
                        var text = instruction.Operand?.ToString() ?? string.Empty;
                        if (string.IsNullOrEmpty(text))
                            continue;

                        Console.WriteLine($"{type.Name} : {method.Name} : {text}");
                        entries.Add(new CsvEntry
                        {
                            ClassName = type.Name,
                            MethodName = method.Name,
                            Text = text,
                            NewText = string.Empty
                        });
                    }
                }
            }
        }

        using var writer = new StreamWriter(csvFilePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(entries);
    }

    static void ImportAndModifyAssembly(AssemblyDefinition assembly, string csvFilePath)
    {
        List<CsvEntry> records;
        using (var reader = new StreamReader(csvFilePath))
        using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
        {
            records = csv.GetRecords<CsvEntry>().ToList();
        }

        foreach (var type in assembly.MainModule.Types)
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode == OpCodes.Ldstr)
                    {
                        var currentText = instruction.Operand?.ToString() ?? string.Empty;
                        var record = records.FirstOrDefault(r =>
                            r.ClassName == type.Name &&
                            r.MethodName == method.Name &&
                            r.Text == currentText);
                        if (record != null && !string.IsNullOrEmpty(record.NewText) &&
                            record.NewText != currentText)
                        {
                            Console.WriteLine($"Updating {type.Name} : {method.Name} : '{currentText}' -> '{record.NewText}'");
                            instruction.Operand = record.NewText;
                        }
                    }
                }
            }
        }
    }

    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: EditDllString.exe [-export | -import]");
            return;
        }

        var mode = args[0].ToLowerInvariant();
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var iniPath = Path.Combine(exeDir, "config.ini");

        if (!File.Exists(iniPath))
        {
            Console.WriteLine("config.ini 파일을 찾을 수 없습니다.");
            return;
        }

        var parser = new FileIniDataParser();
        IniData configData = parser.ReadFile(iniPath);

        var targetDll = configData["Paths"]["TARGET_DLL"];
        var editedDll = configData["Paths"]["EDITED_DLL"];
        var csvFile = configData["Paths"]["CSV_FILE"];
        var assemblyDir = configData["Paths"]["ASSEMBLY_DIR"];

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(assemblyDir);
        var readerParameters = new ReaderParameters { AssemblyResolver = resolver };

        using var assembly = AssemblyDefinition.ReadAssembly(targetDll, readerParameters);

        switch (mode)
        {
            case "-export":
            case "-e":
                ExportLdstrStrings(assembly, csvFile);
                Console.WriteLine("CSV export가 완료되었습니다.");
                break;
            case "-import":
            case "-i":
                ImportAndModifyAssembly(assembly, csvFile);
                assembly.Write(editedDll);
                Console.WriteLine("CSV import 완료, 텍스트가 수정되었습니다!");
                Console.WriteLine("수정된 DLL이 저장되었습니다.");
                break;
            default:
                Console.WriteLine("Usage: EditDllString.exe [-export, -e | -import, -i]");
                break;
        }

        Console.WriteLine("작업 완료!");
    }
}
