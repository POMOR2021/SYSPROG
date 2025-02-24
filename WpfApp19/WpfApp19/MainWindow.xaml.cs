using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace WpfApp19
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource ct;
        private readonly object lockObj = new object();
        private bool isPaus;
        private HashSet<string> forWord;
        private Dictionary<string, int> wordF;
        private const string OutputFold = @"C:\WpfApp19";
        private const string ReportFile = "report.txt";

        public MainWindow()
        {
            InitializeComponent();
            forWord = new HashSet<string>();
            wordF = new Dictionary<string, int>();
            CheckSingleOutp();
        }

        private void CheckSingleOutp()
        {
            if (System.Diagnostics.Process.GetProcessesByName(
                System.IO.Path.GetFileNameWithoutExtension(
                    System.Reflection.Assembly.GetEntryAssembly().Location)).Length > 1)
            {
                MessageBox.Show("Приложение уже запущено!");
                Application.Current.Shutdown();
            }
        }

        private void LoadWordBut_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                forWord = new HashSet<string>(File.ReadAllLines(dialog.FileName));
                ForbWordsTB.Text = string.Join(Environment.NewLine, forWord);
            }
        }

        private async void StartBut_Click(object sender, RoutedEventArgs e)
        {
            if (forWord.Count == 0 && !string.IsNullOrWhiteSpace(ForbWordsTB.Text))
            {
                forWord = new HashSet<string>(ForbWordsTB.Text.Split(new[] { Environment.NewLine }, 
                        StringSplitOptions.RemoveEmptyEntries));
            }
            if (forWord.Count == 0)
            {
                MessageBox.Show("Введите или загрузите запрещенные слова!");
                return;
            }

            ct = new CancellationTokenSource();
            isPaus = false;
            wordF.Clear();
            Directory.CreateDirectory(OutputFold);

            StartButton.IsEnabled = false;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;

            await Task.Run(() => ScanDrives(ct.Token));
        }

        private void ScanDrives(CancellationToken token)
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable);

            int totalFilesProcessed = 0;
            int totalFilesFound = 0;

            void UpdateProgress(int processed, int found)
            {
                totalFilesProcessed += processed;
                totalFilesFound += found;
                Dispatcher.Invoke(() =>
                {
                    if (totalFilesProcessed > 0)
                    {
                        PB.Value = Math.Min((totalFilesProcessed * 100) / totalFilesProcessed, 100);
                    }
                    StatText.Text = $"Обработано файлов: {totalFilesProcessed}";
                });
            }
            foreach (var drive in drives)
            {
                ProcessDirectory(drive.RootDirectory, token, UpdateProgress);
            }

            GenerateReport();
            Dispatcher.Invoke(() => UpdateUICompletion());
        }

        private void ProcessDirectory(DirectoryInfo dir, CancellationToken token, Action<int, int> updateProgress)
        {
            try
            {
                var files = dir.EnumerateFiles().ToList();
                var directories = dir.EnumerateDirectories().ToList();

                foreach (var file in files)
                {
                    lock (lockObj)
                    {
                        if (token.IsCancellationRequested) return;
                        while (isPaus) Thread.Sleep(100);
                    }

                    ProcessFile(file);
                    updateProgress(files.Count, directories.Count);
                }
                foreach (var subDir in directories)
                {
                    ProcessDirectory(subDir, token, updateProgress);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => ResLB.Items.Add($"Ошибка: {ex.Message}"));
            }
        }

        private void ProcessFile(FileInfo file)
        {
            try
            {
                string content = File.ReadAllText(file.FullName);
                var foundWords = forWord.Where(w => content.Contains(w)).ToList();

                if (foundWords.Any())
                {
                    string newContent = content;
                    foreach (var word in foundWords)
                    {
                        newContent = newContent.Replace(word, "*******");
                        lock (lockObj)
                        {
                            wordF[word] = wordF.GetValueOrDefault(word) + 1;
                        }
                    }

                    string outputPath = Path.Combine(OutputFold, file.Name);
                    File.Copy(file.FullName, outputPath, true);
                    File.WriteAllText(Path.Combine(OutputFold, "Modified_" + file.Name), newContent);

                    Dispatcher.Invoke(() =>
                        ResLB.Items.Add($"Найден файл: {file.FullName} ({foundWords.Count} слов)"));
                }
            }

            catch (Exception) { }
        }

        private void GenerateReport()
        {
            using (var writer = new StreamWriter(Path.Combine(OutputFold, ReportFile)))
            {
                writer.WriteLine($"Дата: {DateTime.Now}");
                writer.WriteLine($"Всего найдено файлов: {ResLB.Items.Count}");
                writer.WriteLine("\nТоп-10 запрещенных слов:");
                foreach (var pair in wordF.OrderByDescending(x => x.Value).Take(10))
                {
                    writer.WriteLine($"{pair.Key}: {pair.Value} раз");
                }
            }
        }

        private void UpdateUICompletion()
        {
            StartButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            ObchText.Text = $"Сканирование завершено. Найдено файлов: {ResLB.Items.Count}";
        }

        private void PauseBut_Click(object sender, RoutedEventArgs e)
        {
            isPaus = !isPaus;
            PauseButton.Content = isPaus ? "Возобновить" : "Пауза";
        }

        private void StopBut_Click(object sender, RoutedEventArgs e)
        {
            ct?.Cancel();
            UpdateUICompletion();
        }
    }
}