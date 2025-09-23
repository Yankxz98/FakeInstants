namespace fakeinstants.Models;

public class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#007bff"; // Default blue
    public string Icon { get; set; } = "fas fa-music"; // Default music icon
    public int SoundCount { get; set; }

    public Category()
    {
        Id = Guid.NewGuid().ToString();
    }

    public Category(string name, string description = "")
    {
        Id = Guid.NewGuid().ToString();
        Name = name;
        Description = description;
    }
}

