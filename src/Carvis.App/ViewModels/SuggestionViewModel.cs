namespace Carvis.App.ViewModels;

/// <summary>A card on the empty conversation that fills the prompt with an example.</summary>
public sealed record SuggestionViewModel(string IconKey, string Title, string Example);
