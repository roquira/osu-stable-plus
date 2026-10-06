using System.Text.Json;
using System.Text;
using Osu.StablePlus.Performance;
using Osu.StablePlus.Performance.Engine;

// Private redirected stdin/stdout channel owned by the launching game process.
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
var output = Console.Out;
Console.SetOut(Console.Error); // upstream diagnostics must not corrupt the wire stream
var calculator = new Calculator();
string? line;
while ((line = Console.ReadLine()) != null)
{
    CalculationRequest? request = null;
    CalculationResult result;
    try
    {
        if (line.Length > 1024 * 1024) throw new InvalidDataException("Request too large.");
        // .NET Framework creates its redirected pipe writer with a UTF-8 preamble.
        request = JsonSerializer.Deserialize<CalculationRequest>(line.TrimStart('\uFEFF')) ?? throw new InvalidDataException("Missing request.");
        result = calculator.Calculate(request);
    }
    catch (Exception e) { result = new CalculationResult { Id = request?.Id ?? 0, Error = e.Message }; }
    await output.WriteLineAsync(JsonSerializer.Serialize(result));
    await output.FlushAsync();
}
