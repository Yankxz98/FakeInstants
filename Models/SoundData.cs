namespace fakeinstants.Models;

public class SoundData
{
    public List<Sound> Sounds { get; set; } = new();
    public List<Category> Categories { get; set; } = new();

    // Default categories
    public static readonly List<Category> DefaultCategories = new()
    {
        new Category("Memes", "Sons de memes e virais") { Color = "#ff6b6b", Icon = "fas fa-laugh" },
        new Category("Efeitos", "Efeitos sonoros diversos") { Color = "#4ecdc4", Icon = "fas fa-magic" },
        new Category("Música", "Trechos musicais") { Color = "#45b7d1", Icon = "fas fa-music" },
        new Category("Games", "Sons de jogos") { Color = "#96ceb4", Icon = "fas fa-gamepad" },
        new Category("Outros", "Outros tipos de sons") { Color = "#feca57", Icon = "fas fa-folder" }
    };
}

