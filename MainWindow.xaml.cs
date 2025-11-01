using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace ScreenPDF
{
    public partial class MainWindow : Window
    {
        private bool _isProcessing = false;
        private bool _isScanning = false;
        private string selectedFolderPath = string.Empty;
        private string lastScanFolder = string.Empty;
        private string[] imageFiles;

        public MainWindow()
        {
            InitializeComponent();

            PathToImages.Text = Properties.Settings.Default.SelectedFolder;
            selectedFolderPath = Properties.Settings.Default.SelectedFolder;
            lastScanFolder = Properties.Settings.Default.LastScanFolder;

            this.Loaded += (s, e) => RestoreWindowSizeAndPosition();
            this.Loaded += (s, e) => TxtLeft.Focus();

            Loaded += async (s, e) =>
            {
                await ScanFolderAsync();
            };

            // Подписываемся на события изменения текста для сброса ошибки
            TxtLeft.TextChanged += TextBox_TextChanged;
            TxtRight.TextChanged += TextBox_TextChanged;
        }

        private void RestoreWindowSizeAndPosition()
        {
            try
            {
                var left = Properties.Settings.Default.WindowLeft;
                var top = Properties.Settings.Default.WindowTop;
                var width = Properties.Settings.Default.WindowWidth;
                var height = Properties.Settings.Default.WindowHeight;

                if (width > 0 && height > 0 &&
                    width >= this.MinWidth && height >= this.MinHeight &&
                    width <= SystemParameters.VirtualScreenWidth &&
                    height <= SystemParameters.VirtualScreenHeight)
                {
                    this.Width = width;
                    this.Height = height;
                }
                else
                {
                    this.Width = 520;
                    this.Height = 310;
                }

                if (!double.IsNaN(left) && !double.IsNaN(top) && left >= 0 && top >= 0)
                {
                    var virtualWidth = SystemParameters.VirtualScreenWidth;
                    var virtualHeight = SystemParameters.VirtualScreenHeight;

                    if (left + this.Width <= virtualWidth && top + this.Height <= virtualHeight)
                    {
                        this.Left = left;
                        this.Top = top;
                        return;
                    }
                }

                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                this.Width = 520;
                this.Height = 310;
            }
        }

        // Сброс ошибки при изменении текста
        private void TextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            HideError();
            UpdateStatus(_isScanning ? "Сканирование файлов" : "Готов", 0);
        }

        // Показать ошибку жирным красным текстом
        private void ShowError()
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = "Исправьте цифры";
                TxtStatus.Foreground = new SolidColorBrush(Colors.Red);
                TxtStatus.FontWeight = FontWeights.Bold;
            });
        }

        // Скрыть ошибку и восстановить нормальный статус
        private void HideError()
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                TxtStatus.FontWeight = FontWeights.Normal;

                // Восстанавливаем соответствующий статус
                if (_isScanning)
                {
                    TxtStatus.Text = "Сканирование файлов";
                }
                else
                {
                    TxtStatus.Text = "Готов";
                }
            });
        }

        private bool ValidateNumbers(string leftText, string rightText)
        {
            if (string.IsNullOrWhiteSpace(leftText) || string.IsNullOrWhiteSpace(rightText))
            {
                return false;
            }

            if (!int.TryParse(leftText, out int leftNumber) || !int.TryParse(rightText, out int rightNumber))
            {
                return false;
            }

            if (leftNumber <= 0 || rightNumber <= 0)
            {
                return false;
            }

            int lastFourLeft = leftNumber % 10000;
            int lastFourRight = rightNumber % 10000;

            if (lastFourLeft > lastFourRight)
            {
                return false;
            }

            return true;
        }

        private async Task StartProcessingAsync(string leftValue, string rightValue)
        {
            if (_isProcessing) return;

            _isProcessing = true;
            UpdateStatus("Начало обработки...", 0);

            try
            {
                // Ждем окончания сканирования
                while (_isScanning)
                {
                    await Task.Delay(100);
                }

                // Проверяем числа еще раз (на случай если пользователь изменил их во время ожидания)
                if (!ValidateNumbers(leftValue, rightValue))
                {
                    ShowError();
                    return;
                }

                // Здесь будет основная логика программы
                // await ProcessImagesAsync(leftValue, rightValue);

                UpdateStatus("Обработка завершена", 100);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка: {ex.Message}", 0);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task ScanFolderAsync()
        {
            _isScanning = true;
            UpdateStatus("Сканирование файлов", 5);

            string pathFolder = Properties.Settings.Default.SelectedFolder;
            if (pathFolder == "" || string.IsNullOrWhiteSpace(pathFolder))
            {
                UpdateStatus("Папка не указана", 0);
                _isScanning = false;
                return;
            }

            if (!Directory.Exists(pathFolder))
            {
                UpdateStatus("Указанная папка не найдена", 0);
                _isScanning = false;
                return;
            }

            await Task.Run(() =>
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
                imageFiles = Directory.GetFiles(pathFolder, "*.*", SearchOption.TopDirectoryOnly)
                                      .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                      .ToArray();
            });

            _isScanning = false;
            UpdateStatus("Готов", 0);
        }

        private void UpdateStatus(string message, int progress)
        {
            Dispatcher.Invoke(() =>
            {
                // Не обновляем статус если сейчас показывается ошибка
                if (TxtStatus.Text == "Исправьте цифры" && message != "Исправьте цифры")
                    return;

                TxtStatus.Text = message;
                TxtPercent.Text = progress + "%";
                MainProgress.Value = progress;
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (this.WindowState == WindowState.Normal)
                {
                    Properties.Settings.Default.WindowLeft = this.Left;
                    Properties.Settings.Default.WindowTop = this.Top;
                    Properties.Settings.Default.WindowWidth = this.Width;
                    Properties.Settings.Default.WindowHeight = this.Height;
                }
                else
                {
                    Properties.Settings.Default.WindowLeft = this.RestoreBounds.Left;
                    Properties.Settings.Default.WindowTop = this.RestoreBounds.Top;
                    Properties.Settings.Default.WindowWidth = this.RestoreBounds.Width;
                    Properties.Settings.Default.WindowHeight = this.RestoreBounds.Height;
                }

                Properties.Settings.Default.Save();
            }
            catch { }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    this.DragMove();
                }
                catch { }
            }
        }

        private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку с изображениями";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    selectedFolderPath = dialog.SelectedPath;
                    PathToImages.Text = selectedFolderPath;
                    PathToImages.ToolTip = selectedFolderPath;

                    Properties.Settings.Default.SelectedFolder = selectedFolderPath;
                    Properties.Settings.Default.Save();

                    await ScanFolderAsync();
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            BtnClose.IsEnabled = false;
            var bgEllipse = (Ellipse)BtnClose.Template.FindName("bg", BtnClose);
            if (bgEllipse != null)
            {
                bgEllipse.Fill = new SolidColorBrush(Color.FromRgb(138, 138, 138));
            }
            Task.Run(async () =>
            {
                await Task.Delay(220);
                Dispatcher.Invoke(() =>
                {
                    try { this.Close(); } catch { }
                });
            });
        }

        private void TxtLeft_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TxtRight.Focus();
                TxtRight.SelectAll();
            }
        }

        private async void TxtRight_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_isProcessing) return;

                Keyboard.ClearFocus();

                string leftVal = TxtLeft.Text;
                string rightVal = TxtRight.Text;

                if (!ValidateNumbers(leftVal, rightVal))
                {
                    ShowError();
                    return;
                }

                await StartProcessingAsync(leftVal, rightVal);
            }
        }
    }
}