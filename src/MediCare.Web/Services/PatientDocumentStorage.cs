namespace MediCare.Web.Services;

public class PatientDocumentStorage
{
    public const long MaximumSize = 5 * 1024 * 1024;
    private readonly string _root;
    public PatientDocumentStorage(IWebHostEnvironment environment)
    {
        // Never expose this directory through static-file middleware.
        _root = Path.Combine(environment.ContentRootPath, "App_Data", "PatientDocuments");
    }

    public static string Validate(string fileName, string contentType, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.LongLength > MaximumSize)
            throw new InvalidDataException("Select a nonempty document no larger than 5 MB.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var valid = extension switch
        {
            ".pdf" => contentType == "application/pdf" && bytes.AsSpan().StartsWith("%PDF-"u8),
            ".jpg" or ".jpeg" => contentType == "image/jpeg" && bytes.Length >= 3 &&
                bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            ".png" => contentType == "image/png" && bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}),
            _ => false
        };
        if (!valid) throw new InvalidDataException("Only matching PDF, JPEG and PNG documents are allowed.");
        return extension is ".jpg" or ".jpeg" ? "image/jpeg" : extension == ".png" ? "image/png" : "application/pdf";
    }

    public async Task<(string Name, string Type, long Size)> SaveAsync(IFormFile file)
    {
        if (file.Length <= 0 || file.Length > MaximumSize)
            throw new InvalidDataException("Select a nonempty document no larger than 5 MB.");
        await using var input = file.OpenReadStream();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length + count > MaximumSize) throw new InvalidDataException("Maximum upload size is 5 MB.");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        var type = Validate(file.FileName, file.ContentType, bytes);
        Directory.CreateDirectory(_root);
        var name = Guid.NewGuid().ToString("N");
        var created = false;
        try
        {
            await using var target = new FileStream(PathFor(name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            await target.WriteAsync(bytes);
        }
        catch
        {
            if (created) Delete(name);
            throw;
        }
        return (name, type, bytes.LongLength);
    }

    private string PathFor(string name)
    {
        if (!Guid.TryParseExact(name, "N", out _)) throw new InvalidDataException("Invalid stored document identifier.");
        return Path.Combine(_root, name);
    }
    public Stream Open(string name) => new FileStream(PathFor(name), FileMode.Open, FileAccess.Read, FileShare.Read);
    public void Delete(string name) => File.Delete(PathFor(name));
}
