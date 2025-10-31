using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;



namespace ScreenPDF
{
    public partial class MainWindow : Window
    {
        private bool _isProcessing = false;

        // Путь к выбранной папке с картинками
        private string selectedFolderPath = string.Empty;

        // Путь к последней сканированной папке
        private string lastScanFolder = string.Empty;

        // массив файлов
        private string[] imageFiles;



        public MainWindow()
        {
            InitializeComponent();

            PathToImages.Text = Properties.Settings.Default.SelectedFolder;
            selectedFolderPath = Properties.Settings.Default.SelectedFolder;
            lastScanFolder = Properties.Settings.Default.LastScanFolder;


            // Запомним размеры (фиксированные) на всякий случай, хотя ResizeMode=NoResize
            this.Width = 520;
            this.Height = 310;

            // Ставим фокус на первый текстбокс при создании
            this.Loaded += (s, e) => TxtLeft.Focus();

            Loaded += async (s, e) =>
            {
                await ScanFolderAsync();
            };

        }

        // сканирование файлов при запуске программы
        private async Task ScanFolderAsync()
        {
            string pathFolder = Properties.Settings.Default.SelectedFolder;
            if (pathFolder == "" || string.IsNullOrWhiteSpace(pathFolder))
            {
                UpdateStatus("Папка не указана", 0);
                return;
            }

            if (!Directory.Exists(pathFolder))
            {
                UpdateStatus("Указанная папка не найдена", 0);
                return;
            }

            UpdateStatus("Сканирование папки...", 5);

            await Task.Run(() =>
            {
                // Поиск изображений с нужными расширениями
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
                imageFiles = Directory.GetFiles(pathFolder, "*.*", SearchOption.TopDirectoryOnly)
                                      .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                      .ToArray();
                Task.Delay(100);
            });

            if (imageFiles.Length > 0)
                UpdateStatus($"Найдено {imageFiles.Length} изображений", 0);
            else
                UpdateStatus("Изображения не найдены", 0);
        }

        // обновление статуса в подвале
        private void UpdateStatus(string message, int progress)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = message;   // Текст внизу окна
                TxtPercent.Text = progress.ToString() + "%";
                MainProgress.Value = progress;     // Проценты выполнения
            });
        }



        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Попытка восстановить позицию из Application Settings
            try
            {
                var left = Properties.Settings.Default.WindowLeft;
                var top = Properties.Settings.Default.WindowTop;

                if (!double.IsNaN(left) && !double.IsNaN(top) && left >= 0 && top >= 0)
                {
                    // Проверка, чтобы окно не открылось вне видимой области
                    var virtualWidth = SystemParameters.VirtualScreenWidth;
                    var virtualHeight = SystemParameters.VirtualScreenHeight;

                    if (left + this.Width <= virtualWidth && top + this.Height <= virtualHeight)
                    {
                        this.Left = left;
                        this.Top = top;
                    }
                    else
                    {
                        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }
                }
                else
                {
                    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            catch
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Сохраняем позицию
            try
            {
                Properties.Settings.Default.WindowLeft = this.Left;
                Properties.Settings.Default.WindowTop = this.Top;
                Properties.Settings.Default.Save();
            }
            catch
            {
                // silent
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Драг-движение по заголовку
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

                    // Обновляем ToolTip вручную (если привязка не сработает сразу)
                    PathToImages.ToolTip = selectedFolderPath;

                    Console.WriteLine($"Выбрана папка: {selectedFolderPath}");

                    // Сохраняем путь в настройках
                    Properties.Settings.Default.SelectedFolder = selectedFolderPath;
                    Properties.Settings.Default.Save();

                    await ScanFolderAsync();
                }
            }
        }


        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // Кнопка закрытия: визуальная подсветка на короткое время, затем Close()
            BtnClose.IsEnabled = false;
            // В шаблоне при IsPressed уже меняется цвет, но сделаем явную задержку, чтобы пользователь увидел эффект
            // Меняем цвет крестика на серый
            var bgEllipse = (Ellipse)BtnClose.Template.FindName("bg", BtnClose);
            if (bgEllipse != null)
            {
                bgEllipse.Fill = new SolidColorBrush(Color.FromRgb(138, 138, 138)); // серый
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
                // Переместить фокус в следующий
                TxtRight.Focus();
                TxtRight.SelectAll();
            }
        }


        private void TxtRight_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_isProcessing) return;
                _isProcessing = true;

                // Снимаем фокус
                Keyboard.ClearFocus();

                // Логика
                string leftVal = TxtLeft.Text;
                string rightVal = TxtRight.Text;
                Console.WriteLine($"Start processing: {leftVal} - {rightVal}");



                _isProcessing = false;
            }
        }
    }
}
