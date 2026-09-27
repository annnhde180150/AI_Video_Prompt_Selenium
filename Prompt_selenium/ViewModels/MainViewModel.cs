using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PromptSelenium.Models;
using PromptSelenium.Services;

namespace PromptSelenium.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ExcelService _excelService = new();
    private readonly PromptTemplateService _promptTemplateService = new();
    private readonly ChromeLauncher _chromeLauncher = new();
    private readonly ChatGptBrowserService _browserService = new();
    private readonly StoryAutomationService _automationService;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private string _chromePath = string.Empty;

    [ObservableProperty]
    private int _debugPort = 9222;

    [ObservableProperty]
    private int _workerCount = 5;

    [ObservableProperty]
    private int _responseTimeoutMinutes = 10;

    [ObservableProperty]
    private string _prompt1 = string.Empty;

    [ObservableProperty]
    private string _prompt2 = string.Empty;

    [ObservableProperty]
    private string _prompt3 = string.Empty;

    [ObservableProperty]
    private int _connectedTabs;

    [ObservableProperty]
    private int _completedStories;

    [ObservableProperty]
    private int _totalStories;

    [ObservableProperty]
    private string _statusText = "Sẵn sàng";

    [ObservableProperty]
    private bool _isBusy;

    public MainViewModel()
    {
        _automationService = new StoryAutomationService(_browserService);
        ChromePath = _chromeLauncher.DetectChromePath() ?? string.Empty;
        try
        {
            var prompts = _promptTemplateService.LoadPrompts();
            Prompt1 = prompts.Prompt1;
            Prompt2 = prompts.Prompt2;
            Prompt3 = prompts.Prompt3;
        }
        catch (Exception exception)
        {
            Prompt1 = PromptTemplateService.StoryPlaceholder;
            AddLog($"WARNING: {exception.Message}");
        }

        AddLog("Hãy launch Chrome, đăng nhập ChatGPT và giữ đủ 5 tab trước khi Connect.");
    }

    public ObservableCollection<string> LogEntries { get; } = [];
    public bool IsNotBusy => !IsBusy;
    public string ProgressText => $"{CompletedStories} / {TotalStories}";
    public string ConnectionText => $"Đã kết nối {ConnectedTabs} tab ChatGPT";

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));
    partial void OnCompletedStoriesChanged(int value) => OnPropertyChanged(nameof(ProgressText));
    partial void OnTotalStoriesChanged(int value) => OnPropertyChanged(nameof(ProgressText));
    partial void OnConnectedTabsChanged(int value) => OnPropertyChanged(nameof(ConnectionText));

    [RelayCommand]
    private void BrowseInput()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn Excel chứa cột Story",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        InputPath = dialog.FileName;
        OutputPath = Path.Combine(
            Path.GetDirectoryName(InputPath)!,
            $"{Path.GetFileNameWithoutExtension(InputPath)}_final.xlsx");
        AddLog($"Input: {InputPath}");
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Chọn file Excel kết quả",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(OutputPath)
                ? "stories_final.xlsx"
                : Path.GetFileName(OutputPath),
            InitialDirectory = string.IsNullOrWhiteSpace(OutputPath)
                ? null
                : Path.GetDirectoryName(OutputPath)
        };
        if (dialog.ShowDialog() == true)
        {
            OutputPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseChrome()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn chrome.exe",
            Filter = "Google Chrome (chrome.exe)|chrome.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            ChromePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void LaunchChrome()
    {
        try
        {
            ValidateBrowserSettings();
            _chromeLauncher.Launch(ChromePath, DebugPort, WorkerCount);
            StatusText = "Chrome đã mở - hãy đăng nhập ChatGPT";
            AddLog($"Đã launch Chrome với {WorkerCount} tab trên debug port {DebugPort}.");
            AddLog("Sau khi đăng nhập và thấy ô nhập prompt ở mọi tab, bấm Connect.");
        }
        catch (Exception exception)
        {
            ShowError("Không thể launch Chrome", exception);
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Đang kết nối Chrome...";
        try
        {
            ValidateBrowserSettings();
            ConnectedTabs = await _browserService.ConnectAsync(DebugPort, CancellationToken.None);
            await _browserService.VerifyTabsReadyAsync(WorkerCount, CancellationToken.None);
            StatusText = $"Đã kết nối {ConnectedTabs} tab";
            AddLog($"Kết nối thành công. Tìm thấy {ConnectedTabs} tab ChatGPT; dùng {WorkerCount} worker.");
        }
        catch (Exception exception)
        {
            ConnectedTabs = 0;
            ShowError("Kết nối thất bại", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            ValidateAll();
        }
        catch (Exception exception)
        {
            ShowError("Dữ liệu chưa hợp lệ", exception);
            return;
        }

        IsBusy = true;
        CompletedStories = 0;
        TotalStories = 0;
        _cancellationTokenSource = new CancellationTokenSource();
        LogEntries.Clear();

        try
        {
            StatusText = "Đang đọc Excel...";
            var stories = await Task.Run(
                () => _excelService.ReadStories(InputPath),
                _cancellationTokenSource.Token);
            TotalStories = stories.Count;
            AddLog($"Đã đọc {stories.Count} stories.");

            if (ConnectedTabs < WorkerCount)
            {
                StatusText = "Đang kết nối lại Chrome...";
                ConnectedTabs = await _browserService.ConnectAsync(DebugPort, _cancellationTokenSource.Token);
            }

            await _browserService.VerifyTabsReadyAsync(WorkerCount, _cancellationTokenSource.Token);
            var prompts = new PromptSet
            {
                Prompt1 = Prompt1,
                Prompt2 = Prompt2,
                Prompt3 = Prompt3
            };
            var progress = new Progress<AutomationProgress>(update =>
            {
                CompletedStories = update.Completed;
                TotalStories = update.Total;
                StatusText = update.Message;
                AddLog(update.Message);
            });

            var results = await _automationService.ProcessAsync(
                stories,
                prompts,
                WorkerCount,
                TimeSpan.FromMinutes(ResponseTimeoutMinutes),
                progress,
                _cancellationTokenSource.Token);

            StatusText = "Đang ghi Excel kết quả...";
            await Task.Run(
                () => _excelService.WriteResults(OutputPath, results),
                _cancellationTokenSource.Token);
            CompletedStories = TotalStories;
            StatusText = "Hoàn thành";
            AddLog($"Đã lưu: {OutputPath}");
            MessageBox.Show(
                $"Đã xử lý {results.Count} stories.\n\n{OutputPath}",
                "Hoàn thành",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Đã hủy";
            AddLog("Batch đã được hủy; không xuất file final không đầy đủ.");
        }
        catch (Exception exception)
        {
            ShowError("Automation thất bại", exception);
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cancellationTokenSource is null)
        {
            return;
        }

        StatusText = "Đang hủy...";
        AddLog("Đã yêu cầu hủy.");
        _cancellationTokenSource.Cancel();
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _browserService.Dispose();
    }

    private void ValidateBrowserSettings()
    {
        if (string.IsNullOrWhiteSpace(ChromePath) || !File.Exists(ChromePath))
        {
            throw new InvalidDataException("Hãy chọn đường dẫn chrome.exe hợp lệ.");
        }

        if (DebugPort is < 1024 or > 65535)
        {
            throw new InvalidDataException("Debug port phải nằm trong khoảng 1024–65535.");
        }

        if (WorkerCount is < 1 or > 5)
        {
            throw new InvalidDataException("Số worker phải từ 1 đến 5.");
        }
    }

    private void ValidateAll()
    {
        ValidateBrowserSettings();
        if (!File.Exists(InputPath) || !string.Equals(Path.GetExtension(InputPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Hãy chọn file input .xlsx hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(OutputPath)
            || !string.Equals(Path.GetExtension(OutputPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Hãy chọn file output .xlsx hợp lệ.");
        }

        if (string.Equals(
                Path.GetFullPath(InputPath),
                Path.GetFullPath(OutputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("File output phải khác file input để tránh ghi đè dữ liệu gốc.");
        }

        if (ResponseTimeoutMinutes is < 1 or > 60)
        {
            throw new InvalidDataException("Timeout phải từ 1 đến 60 phút.");
        }

        PromptTemplateService.Validate(new PromptSet
        {
            Prompt1 = Prompt1,
            Prompt2 = Prompt2,
            Prompt3 = Prompt3
        });
    }

    private void ShowError(string title, Exception exception)
    {
        StatusText = title;
        AddLog($"ERROR: {exception.Message}");
        MessageBox.Show(exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void AddLog(string message)
    {
        LogEntries.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (LogEntries.Count > 1_000)
        {
            LogEntries.RemoveAt(0);
        }
    }
}
