using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Models;
using Pos.Core.Repositories;

namespace Pos.App.ViewModels;

public partial class NotesViewModel : ObservableObject
{
    private const string Key = "pos_notes";
    private readonly AppSettingsRepository _settings;
    private readonly QuickNotesRepository _quickNotesRepo;

    [ObservableProperty] private string _newNoteText = "";

    public ObservableCollection<NoteItem> Notes { get; } = new();
    public ObservableCollection<QuickNote> QuickOrderNotes { get; } = new();

    public int NoteCount => Notes.Count;
    public bool IsEmpty => Notes.Count == 0;
    public bool HasQuickNotes => QuickOrderNotes.Count > 0;

    public NotesViewModel(AppSettingsRepository settings, QuickNotesRepository quickNotesRepo)
    {
        _settings = settings;
        _quickNotesRepo = quickNotesRepo;

        var saved = _settings.GetJson<List<NoteItem>>(Key);
        if (saved != null)
        {
            foreach (var n in saved.OrderByDescending(n => n.CreatedAt)) Notes.Add(n);
        }
        LoadQuickNotes();
        RaiseCounts();
    }

    public void LoadQuickNotes()
    {
        QuickOrderNotes.Clear();
        foreach (var n in _quickNotesRepo.GetNotes())
        {
            QuickOrderNotes.Add(n);
        }
        OnPropertyChanged(nameof(HasQuickNotes));
    }

    [RelayCommand]
    private void AddNote()
    {
        var text = (NewNoteText ?? "").Trim();
        if (text.Length == 0) return;
        Notes.Insert(0, new NoteItem
        {
            Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Text = text,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
        NewNoteText = "";
        Persist();
    }

    [RelayCommand]
    private void DeleteNote(NoteItem note)
    {
        Notes.Remove(note);
        Persist();
    }

    [RelayCommand]
    private void DeleteQuickOrderNote(QuickNote note)
    {
        if (note == null) return;
        _quickNotesRepo.DeleteNote(note.Id);
        LoadQuickNotes();
    }

    private void Persist()
    {
        _settings.SetJson(Key, Notes.ToList());
        RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(NoteCount));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class NoteItem
{
    public long Id { get; set; }
    public string Text { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string CreatedShort =>
        DateTime.TryParse(CreatedAt, out var d) ? d.ToString("dd MMM yyyy, hh:mm tt") : CreatedAt;
}
