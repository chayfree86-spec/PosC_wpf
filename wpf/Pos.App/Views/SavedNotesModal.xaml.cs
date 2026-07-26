using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Pos.Core.Models;
using Pos.Core.Repositories;

namespace Pos.App.Views;

public enum NoteActionType
{
    None,
    Edit,
    Transfer
}

public sealed class NoteActionResult
{
    public NoteActionType Action { get; set; } = NoteActionType.None;
    public QuickNote? SelectedNote { get; set; }
}

public partial class SavedNotesModal : Window
{
    private readonly QuickNotesRepository _notesRepo;
    private List<QuickNote> _notesList = new();

    public NoteActionResult Result { get; private set; } = new();

    public SavedNotesModal(QuickNotesRepository notesRepo)
    {
        InitializeComponent();
        _notesRepo = notesRepo;
        RefreshNotes();
    }

    private void RefreshNotes()
    {
        _notesList = _notesRepo.GetNotes();
        LstNotes.ItemsSource = _notesList;
        TxtNotesCount.Text = _notesList.Count.ToString();

        if (_notesList.Count == 0)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            NotesScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            NotesScrollViewer.Visibility = Visibility.Visible;
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuickNote note })
        {
            Result = new NoteActionResult { Action = NoteActionType.Edit, SelectedNote = note };
            DialogResult = true;
            Close();
        }
    }

    private void BtnTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuickNote note })
        {
            Result = new NoteActionResult { Action = NoteActionType.Transfer, SelectedNote = note };
            DialogResult = true;
            Close();
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuickNote note })
        {
            var displayName = string.IsNullOrWhiteSpace(note.CustomerName) ? $"Quick Note #{note.Id}" : note.CustomerName;
            if (ThemeMessageBox.Show(this, $"Are you sure you want to DELETE note for '{displayName}'?", "Confirm Delete Note", "yesno") == true)
            {
                _notesRepo.DeleteNote(note.Id);
                RefreshNotes();
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
