using System.Windows;
using System.Windows.Controls;
using Pos.Core.Models;

namespace Pos.App.Views;

public partial class NotesView : UserControl
{
    public NotesView() => InitializeComponent();

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuickNote note } && Window.GetWindow(this) is MainWindow win)
        {
            win.EditQuickNoteFromNotesView(note);
        }
    }

    private void BtnTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuickNote note } && Window.GetWindow(this) is MainWindow win)
        {
            win.TransferQuickNoteFromNotesView(note);
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: QuickNote note } && Window.GetWindow(this) is MainWindow win)
        {
            win.DeleteQuickNoteFromNotesView(note);
        }
    }
}
