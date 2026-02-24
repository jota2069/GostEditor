using System;
using Avalonia.Controls;
using GostEditor.UI.ViewModels;

namespace GostEditor.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Если раздел уже выбран при загрузке — загружаем сразу.
            if (viewModel.SelectedSection is not null)
            {
                SectionEditor.LoadSection(viewModel.SelectedSection);
            }
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedSection))
        {
            MainWindowViewModel? vm = DataContext as MainWindowViewModel;

            if (vm?.SelectedSection is not null)
            {
                SectionEditor.LoadSection(vm.SelectedSection);
            }
        }
    }
}
