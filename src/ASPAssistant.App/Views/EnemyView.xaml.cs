// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Windows.Controls;
using System.Windows;

namespace ASPAssistant.App.Views;

public partial class EnemyView : UserControl
{
    public event Action? RecognitionRequested;

    public EnemyView() => InitializeComponent();

    private void OnRecognizeEnemy(object sender, RoutedEventArgs e) => RecognitionRequested?.Invoke();
}
