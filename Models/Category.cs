namespace fakeinstants.Models;

public class Category
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#007bff"; // Default blue
    public string Icon { get; set; } = "fas fa-music"; // Default music icon
    public int SoundCount { get; set; }

    public Category()
    {
    }

    public Category(string name, string description = "")
    {
        Name = name;
        Description = description;
        Id = GenerateIdFromName(name);
    }

    public static string GenerateIdFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "categoria";

        // Remove acentos e caracteres especiais, converte para minúsculas
        var id = name.ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);
        
        var chars = id.ToCharArray()
            .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
            .ToArray();
        
        id = new string(chars);
        
        // Substitui espaços por hífens
        id = System.Text.RegularExpressions.Regex.Replace(id, @"\s+", "-");
        
        // Remove hífens duplicados
        id = System.Text.RegularExpressions.Regex.Replace(id, @"-+", "-");
        
        // Remove hífens no início e fim
        id = id.Trim('-');

        return string.IsNullOrEmpty(id) ? "categoria" : id;
    }
}

