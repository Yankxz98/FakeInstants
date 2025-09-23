namespace fakeinstants.Models;

public class AudioSettings
{
    public double Volume { get; set; } = 1.0;
    public bool Autoplay { get; set; } = false;
    public bool Repeat { get; set; } = false;
    public bool Shuffle { get; set; } = false;
}

public class UiSettings
{
    public string Theme { get; set; } = "dark"; // "dark" or "light"
    public string GridSize { get; set; } = "medium"; // "small", "medium", "large"
    public bool ShowDescriptions { get; set; } = true;
}

public class AppSettings
{
    public AudioSettings AudioSettings { get; set; } = new();
    public UiSettings UiSettings { get; set; } = new();
}

