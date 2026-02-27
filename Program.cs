using ObjectIR;
using ObjectIR.Core.Builder;
using ObjectIR.Core.Composition;
using ObjectIR.Core.IR;
using ObjectIR.Core.Serialization;
using ObjectIR.Stdio;
using Math = ObjectIR.Stdio.Math;
namespace OCRuntime
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Welcome to the ObjectIR Runtime!");
            // parse the command line arguments
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: oir <path to ObjectIR file>");
                return;
            }
            string path = args[0];
            if (!File.Exists(path))
            {
                Console.WriteLine($"Error: File '{path}' does not exist.");
                return;
            }
            try
            {
                // load the ObjectIR file
                var ir  = new IRRuntime(File.ReadAllText(path));
                ir.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}