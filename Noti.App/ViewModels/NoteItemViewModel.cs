using CommunityToolkit.Mvvm.ComponentModel;
using Noti.Core;

namespace Noti.App.ViewModels;

public sealed class NoteItemViewModel : ObservableObject
{
    private bool _isSelected;

    public NoteItemViewModel(Note model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
    }

    public Note Model { get; }

    public string Id => Model.Id;
    public string Title => Model.Title;
    public string Snippet => Model.Snippet;

    public string Content
    {
        get => Model.Content;
        set
        {
            if (Model.Content == value)
                return;

            Model.Content = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Snippet));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(SelectionOpacity));
        }
    }

    public double SelectionOpacity => _isSelected ? 1.0 : 0.0;
}
