using System;
using Avalonia.Controls;
using GostEditor.UI.ViewModels;
using Avalonia.Input;
using Avalonia.Interactivity;

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

    private void OnBoldClick(object? sender, RoutedEventArgs e)
    {
        WrapSelectedText("**", "**");
    }

    private void OnItalicClick(object? sender, RoutedEventArgs e)
    {
        WrapSelectedText("*", "*");
    }

    private void WrapSelectedText(string prefix, string suffix)
    {
        // ИСПРАВЛЕНИЕ: Используем интерфейс IFocusManager
        IFocusManager? focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        IInputElement? focusedElement = focusManager?.GetFocusedElement();

        // Проверяем, что курсор сейчас стоит именно в нашем текстовом поле на листе А4
        if (focusedElement is TextBox textBox && textBox.Name == "PageTextBox")
        {
            string text = textBox.Text ?? string.Empty;
            int start = textBox.SelectionStart;
            int end = textBox.SelectionEnd;

            if (start == end) return; // Если ничего не выделено - выходим

            // Если пользователь выделял текст мышкой справа налево (индексы перепутаны)
            if (start > end)
            {
                int temp = start;
                start = end;
                end = temp;
            }

            string selected = text.Substring(start, end - start);
            string newText = text.Remove(start, end - start).Insert(start, $"{prefix}{selected}{suffix}");

            textBox.Text = newText;

            // Возвращаем выделение обратно, чтобы пользователь видел результат
            textBox.SelectionStart = start;
            textBox.SelectionEnd = start + selected.Length + prefix.Length + suffix.Length;
        }
    }

}
