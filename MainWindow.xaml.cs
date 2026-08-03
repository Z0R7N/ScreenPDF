using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Drawing;
using System.Drawing.Imaging;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using ImageFormat = System.Drawing.Imaging.ImageFormat;


namespace ScreenPDF
{
    public partial class MainWindow : Window
    {
        // Флаг, указывающий что идет процесс обработки изображений
        private bool _isProcessing;

        // Флаг, указывающий что идет сканирование папки с файлами
        private bool _isScanning;

        // Массив путей к найденным файлам изображений
        private string[] _imageFiles;

        // Список годов (номеров папок), которые не нужно сканировать
        private List<int> _excludedYears;

        /// <summary>
        /// Конструктор главного окна приложения
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // Загружаем последнюю выбранную папку из настроек
            PathToImages.Text = Properties.Settings.Default.SelectedFolder;

            // Загружаем список исключенных папок
            _excludedYears = LoadExcludedYears();

            //---------------------------
            // Удаляем текущий год из исключений, если он там есть
            int currentYear = DateTime.Now.Year % 100; // Берем последние 2 цифры (например, 26)
            // Показываем список исключений ДО удаления
            string excludedBefore = _excludedYears.Count > 0 ? string.Join(", ", _excludedYears) : "(пусто)";
            if (_excludedYears.Contains(currentYear))
            {
                _excludedYears.Remove(currentYear);
                SaveExcludedYears();
            }
            // Показываем список исключений ПОСЛЕ удаления
            string excludedAfter = _excludedYears.Count > 0 ? string.Join(", ", _excludedYears) : "(пусто)";
            //----------------------

            // Очищаем список исключений (закомменитировать после отладки приложения)
            ClearExcludedYears();

            // Обработчик события загрузки окна
            Loaded += async (s, e) =>
            {
                RestoreWindowPosition(); // Восстанавливаем позицию и размер окна
                TxtLeft.Focus(); // Устанавливаем фокус на первое поле ввода
                await ScanFolderAsync(); // Сканируем папку при запуске
            };

            // Подписываемся на изменение текста для сброса ошибок
            TxtLeft.TextChanged += (s, e) => ClearError();
            TxtRight.TextChanged += (s, e) => ClearError();
        }

        /// <summary>
        /// Очищает список исключённых годов (всегда сканируем все папки)
        /// </summary>
        private void ClearExcludedYears()
        {
            _excludedYears.Clear();
            Properties.Settings.Default.ExcludedYears = "";
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Восстанавливает сохраненную позицию и размер окна
        /// </summary>
        private void RestoreWindowPosition()
        {
            var settings = Properties.Settings.Default;

            // Восстанавливаем размер окна с проверкой границ
            if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
            {
                Width = Math.Clamp(settings.WindowWidth, MinWidth, SystemParameters.VirtualScreenWidth);
                Height = Math.Clamp(settings.WindowHeight, MinHeight, SystemParameters.VirtualScreenHeight);
            }

            // Восстанавливаем позицию окна, если она находится в пределах экрана
            if (settings.WindowLeft >= 0 && settings.WindowTop >= 0 &&
                settings.WindowLeft + Width <= SystemParameters.VirtualScreenWidth &&
                settings.WindowTop + Height <= SystemParameters.VirtualScreenHeight)
            {
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
            }
            else
            {
                // Если позиция за пределами экрана, центрируем окно
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        /// <summary>
        /// Сбрасывает отображение ошибки и возвращает нормальный вид статуса
        /// </summary>
        private void ClearError()
        {
            // Возвращаем обычный цвет текста
            TxtStatus.Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 51, 51));
            TxtStatus.FontWeight = FontWeights.Normal;

            // Обновляем статус в зависимости от текущего состояния
            UpdateStatus(_isScanning ? "Сканирование файлов" : "Готов", 0);
        }

        /// <summary>
        /// Отображает сообщение об ошибке красным жирным текстом
        /// </summary>
        /// <param name="message">Текст сообщения об ошибке</param>
        private void ShowError(string message = "Исправьте цифры")
        {
            TxtStatus.Text = message;
            TxtStatus.Foreground = System.Windows.Media.Brushes.Red;
            TxtStatus.FontWeight = FontWeights.Bold;
        }

        /// <summary>
        /// Проверяет корректность введенных чисел
        /// </summary>
        /// <param name="leftText">Текст из левого поля ввода</param>
        /// <param name="rightText">Текст из правого поля ввода</param>
        /// <returns>True, если числа валидны</returns>
        private bool ValidateNumbers(string leftText, string rightText)
        {
            // Проверяем, что оба значения можно преобразовать в целые числа
            if (!int.TryParse(leftText, out int left) || !int.TryParse(rightText, out int right))
                return false;

            // Проверяем, что числа положительные
            if (left <= 0 || right <= 0)
                return false;

            // Проверяем длину номера (минимум 8 цифр: ГГППДДДД)
            if (leftText.Length < 8 || leftText.Length > 8 || rightText.Length < 8 || rightText.Length > 8)
                return false;

            // Проверяем, что последние 4 цифры левого числа <= последних 4 цифр правого
            return (left % 10000) <= (right % 10000);
        }

        /// <summary>
        /// Запускает процесс обработки изображений
        /// </summary>
        /// <param name="leftValue">Левое число диапазона</param>
        /// <param name="rightValue">Правое число диапазона</param>
        private async Task StartProcessingAsync(string leftValue, string rightValue)
        {
            // Если уже идет обработка, выходим
            if (_isProcessing) return;

            // Проверяем, что папка указана
            string folder = Properties.Settings.Default.SelectedFolder;
            if (string.IsNullOrWhiteSpace(folder))
            {
                ShowError("Выберите папку через кнопку Обзор");
                return;
            }

            // Проверяем, что папка существует
            if (!Directory.Exists(folder))
            {
                ShowError("Указанная папка не найдена");
                return;
            }

            _isProcessing = true;
            UpdateStatus("Начало обработки...", 0);

            try
            {
                // Ждем завершения сканирования папки
                while (_isScanning)
                    await Task.Delay(100);

                // Повторно проверяем числа (на случай если пользователь изменил их)
                if (!ValidateNumbers(leftValue, rightValue))
                {
                    ShowError();
                    return;
                }

                // Основная логика обработки изображений.
                // Метод теперь возвращает true/false — успешно завершилась обработка или нет.
                bool success = await ProcessImagesAsync(leftValue, rightValue);

                // Если внутри произошла ошибка (не хватает файлов, дубликат номера,
                // ошибка создания PDF и т.д.) — она уже показана пользователю через ShowError.
                // Приложение НЕ закрываем, чтобы пользователь успел прочитать ошибку.
                if (!success)
                {
                    return;
                }

                UpdateStatus("Обработка завершена", 100);

                // Пауза 2 секунды, чтобы пользователь успел увидеть "Обработка завершена"
                await Task.Delay(2000);

                // Закрываем приложение (только если всё прошло успешно)
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка: {ex.Message}");
            }
            finally
            {
                // Снимаем флаг обработки в любом случае
                _isProcessing = false;
            }
        }

        /// <summary>
        /// Загружает список исключенных годов из настроек
        /// </summary>
        private void SaveExcludedYears()
        {
            Properties.Settings.Default.ExcludedYears = string.Join(",", _excludedYears);
            Properties.Settings.Default.Save();
        }

        private bool IsFolderProcessed(string folderPath)
        {
            try
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
                var imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToArray();

                if (imageFiles.Length == 0)
                    return false;

                // Если есть хотя бы один файл, начинающийся с буквы - папка НЕ обработана
                bool hasLetterStart = imageFiles.Any(f => !char.IsDigit(Path.GetFileName(f)[0]));

                return !hasLetterStart; // true только если все файлы начинаются с цифры
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Получает список папок-годов из основной папки
        /// </summary>
        private List<string> GetYearFolders(string rootFolder)
        {
            try
            {
                return Directory.GetDirectories(rootFolder)
                    .Where(dir =>
                    {
                        string folderName = Path.GetFileName(dir);
                        return int.TryParse(folderName, out _);
                    })
                    .OrderBy(dir => int.Parse(Path.GetFileName(dir)))
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Асинхронно сканирует выбранную папку и находит файлы изображений
        /// Исключает папки из списка _excludedYears и автоматически добавляет обработанные папки
        /// </summary>
        private async Task ScanFolderAsync()
        {
            _isScanning = true;
            UpdateStatus("Сканирование файлов", 5);

            string folder = Properties.Settings.Default.SelectedFolder;

            if (string.IsNullOrWhiteSpace(folder))
            {
                UpdateStatus("Папка не указана", 0);
                _isScanning = false;
                return;
            }

            if (!Directory.Exists(folder))
            {
                UpdateStatus("Папка не найдена", 0);
                _isScanning = false;
                return;
            }

            await Task.Run(() =>
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
                var allFiles = new List<string>();
                bool excludedListChanged = false;

                var yearFolders = GetYearFolders(folder);

                // Отладка: выводим количество найденных папок-годов
                Dispatcher.Invoke(() => UpdateStatus($"Найдено папок-годов: {yearFolders.Count}", 10));
                System.Threading.Thread.Sleep(500); // Чтобы успеть увидеть

                foreach (var yearFolder in yearFolders)
                {
                    string yearName = Path.GetFileName(yearFolder);

                    if (!int.TryParse(yearName, out int year))
                        continue;

                    if (_excludedYears.Contains(year))
                    {
                        Dispatcher.Invoke(() => UpdateStatus($"Пропуск года {year} (исключен)", 15));
                        System.Threading.Thread.Sleep(300);
                        continue;
                    }

                    if (IsFolderProcessed(yearFolder))
                    {
                        _excludedYears.Add(year);
                        excludedListChanged = true;
                        Dispatcher.Invoke(() => UpdateStatus($"Папка {year} обработана, добавлена в исключения", 20));
                        System.Threading.Thread.Sleep(300);
                        continue;
                    }

                    Dispatcher.Invoke(() => UpdateStatus($"Сканирование года {year}...", 25));

                    var files = Directory.GetFiles(yearFolder, "*.*", SearchOption.AllDirectories)
                        .Where(f =>
                        {
                            if (!extensions.Contains(Path.GetExtension(f).ToLower()))
                                return false;

                            string fileName = Path.GetFileName(f);

                            if (char.IsDigit(fileName[0]))
                                return false;

                            return true;
                        })
                        .ToArray();




                    Dispatcher.Invoke(() => UpdateStatus($"Год {year}: найдено {files.Length} файлов", 30));
                    System.Threading.Thread.Sleep(300);

                    allFiles.AddRange(files);
                }

                if (excludedListChanged)
                {
                    Dispatcher.Invoke(() => SaveExcludedYears());
                }

                // Сортируем файлы по пути (год/дата/имя файла)
                _imageFiles = allFiles
                    .OrderBy(f => f)
                    .ToArray();
            });
            _isScanning = false;
            UpdateStatus($"Найдено файлов: {_imageFiles.Length}", 0);
        }

        /// <summary>
        /// Проверяет существование файла с таким именем и показывает предупреждение
        /// </summary>
        /// <param name="newFilePath">Путь к новому файлу</param>
        /// <param name="number">Номер файла</param>
        /// <returns>True если файл существует</returns>
        private bool CheckFileExists(string newFilePath, string number)
        {
            if (File.Exists(newFilePath))
            {
                Dispatcher.Invoke(() =>
                {
                    // Показываем сообщение
                    MessageBox.Show(
                        $"Файл с номером {number} уже существует!\n\nПуть: {newFilePath}",
                        "Дубликат номера",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // Открываем папку с выделенным файлом
                    try
                    {
                        string argument = $"/select, \"{newFilePath}\"";
                        System.Diagnostics.Process.Start("explorer.exe", argument);
                    }
                    catch
                    {
                        // Если не удалось открыть с выделением, просто открываем папку
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(newFilePath));
                        }
                        catch { }
                    }
                });
                return true;
            }
            return false;
        }

        /// <summary>
        /// Обновляет статусную строку и прогресс-бар
        /// </summary>
        /// <param name="message">Текст сообщения статуса</param>
        /// <param name="progress">Процент выполнения (0-100)</param>
        private void UpdateStatus(string message, int progress)
        {
            Dispatcher.Invoke(() =>
            {
                // Не перезаписываем сообщение об ошибке обычным статусом
                if (TxtStatus.Text == "Исправьте цифры" && message != "Исправьте цифры")
                    return;

                TxtStatus.Text = message;
                TxtPercent.Text = $"{progress}%";
                MainProgress.Value = progress;

                // Сбрасываем стиль ошибки (красный жирный текст)
                // если это не сообщение об ошибке
                if (!message.StartsWith("Ошибка") &&
                    !message.StartsWith("Остановлено") &&
                    !message.StartsWith("Недостаточно") &&
                    !message.StartsWith("Исправьте") &&
                    !message.StartsWith("Выберите"))
                {
                    TxtStatus.Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 51, 51));
                    TxtStatus.FontWeight = FontWeights.Normal;
                }
            });
        }

        /// <summary>
        /// Обработчик закрытия окна - сохраняет позицию и размер
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var settings = Properties.Settings.Default;

            // Получаем границы окна (текущие или восстановленные, если окно свернуто/развернуто)
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;

            // Сохраняем позицию и размер окна
            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
            settings.Save();
        }

        /// <summary>
        /// Обработчик перетаскивания окна за заголовок
        /// </summary>
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        /// <summary>
        /// Обработчик нажатия кнопки "Обзор" - открывает диалог выбора папки
        /// </summary>
        private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку с изображениями";

                // Если пользователь выбрал папку
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Отображаем выбранный путь
                    PathToImages.Text = dialog.SelectedPath;

                    // Сохраняем путь в настройках
                    Properties.Settings.Default.SelectedFolder = dialog.SelectedPath;
                    Properties.Settings.Default.Save();

                    // Запускаем сканирование новой папки
                    await ScanFolderAsync();
                }
            }
        }

        /// <summary>
        /// Обработчик нажатия кнопки закрытия окна
        /// </summary>
        private async void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // Отключаем кнопку чтобы избежать повторных нажатий
            BtnClose.IsEnabled = false;

            // Небольшая задержка для визуального эффекта
            await Task.Delay(220);

            // Закрываем окно
            Close();
        }

        /// <summary>
        /// Обработчик нажатия клавиш в левом поле ввода
        /// </summary>
        private void TxtLeft_KeyDown(object sender, KeyEventArgs e)
        {
            // При нажатии Enter переходим к правому полю
            if (e.Key == Key.Enter)
            {
                TxtRight.Focus();
                TxtRight.SelectAll();
            }
        }

        /// <summary>
        /// Загружает список исключенных годов из настроек
        /// </summary>
        /// <returns>Список годов для исключения из сканирования</returns>
        private List<int> LoadExcludedYears()
        {
            string excluded = Properties.Settings.Default.ExcludedYears ?? "";

            if (string.IsNullOrWhiteSpace(excluded))
                return new List<int>();

            try
            {
                return excluded.Split(',')
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(int.Parse)
                    .ToList();
            }
            catch
            {
                return new List<int>();
            }
        }

        /// <summary>
        /// Генерирует следующий номер детали по правилам партий
        /// </summary>
        /// <param name="currentNumber">Текущий номер (например 25514461)</param>
        /// <returns>Следующий номер с учетом партий</returns>
        private string GetNextNumber(string currentNumber)
        {
            if (string.IsNullOrWhiteSpace(currentNumber) || currentNumber.Length != 8)
                throw new ArgumentException("Номер должен состоять из 8 цифр.");

            // Разбираем номер: ГГ ПП ДДДД
            int year = int.Parse(currentNumber.Substring(0, 2));
            int batch = int.Parse(currentNumber.Substring(2, 2));
            int detail = int.Parse(currentNumber.Substring(4, 4));

            int previousDetail = detail;

            // Увеличиваем номер детали
            detail++;

            // Если началась новая десятка (например 5050 → 5051)
            if (detail % 10 == 1 && detail > previousDetail)
            {
                batch++;

                // ❗ После 99 сразу 01 (нулевой партии нет)
                if (batch > 99)
                {
                    batch = 1;
                }
            }

            // Формируем новый номер
            return $"{year:D2}{batch:D2}{detail:D4}";
        }

        /// <summary>
        /// Конвертирует изображение осциллографа для ч/б печати
        /// Преобразует в оттенки серого и применяет негатив
        /// </summary>
        private Bitmap PrepareImageForPrinting(string imagePath)
        {
            using (var original = new Bitmap(imagePath))
            {
                // Проверяем левый нижний угол
                DrawingColor checkPixel = original.GetPixel(10, original.Height - 10);

                // Если яркость >= 50%, изображение уже готово (светлый фон)
                if (checkPixel.GetBrightness() >= 0.5f)
                {
                    return new Bitmap(original);
                }

                // Создаем результат того же размера
                var result = new Bitmap(original.Width, original.Height);

                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        DrawingColor pixel = original.GetPixel(x, y);

                        // Шаг 1: Конвертируем в оттенки серого (Grayscale)
                        // Используем стандартную формулу: 0.299*R + 0.587*G + 0.114*B
                        int grayValue = (int)(pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114);

                        // Шаг 2: Применяем негатив (инвертируем)
                        int invertedValue = 255 - grayValue;

                        // Устанавливаем новый цвет (серый)
                        result.SetPixel(x, y, DrawingColor.FromArgb(invertedValue, invertedValue, invertedValue));
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Обработчик клика по статусной строке - копирует текст в буфер обмена
        /// </summary>
        private void TxtStatus_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TxtStatus.Text))
            {
                try
                {
                    Clipboard.SetText(TxtStatus.Text);

                    // Сохраняем текущий текст и цвет
                    string originalText = TxtStatus.Text;
                    var originalBrush = TxtStatus.Foreground;

                    // Показываем что скопировано
                    TxtStatus.Text = "✓ Скопировано";
                    TxtStatus.Foreground = new SolidColorBrush(MediaColor.FromRgb(76, 175, 80));

                    // Через секунду возвращаем обратно
                    Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        Dispatcher.Invoke(() =>
                        {
                            TxtStatus.Text = originalText;
                            TxtStatus.Foreground = originalBrush;
                        });
                    });
                }
                catch
                {
                    // Если не удалось скопировать - ничего не делаем
                }
            }
        }

        /// <summary>
        /// Обработчик нажатия клавиш в правом поле ввода
        /// </summary>
        private async void TxtRight_KeyDown(object sender, KeyEventArgs e)
        {
            // При нажатии Enter запускаем обработку
            if (e.Key == Key.Enter && !_isProcessing)
            {
                // Убираем фокус с полей ввода
                Keyboard.ClearFocus();

                // Проверяем корректность введенных чисел
                if (!ValidateNumbers(TxtLeft.Text, TxtRight.Text))
                {
                    ShowError();
                    return;
                }

                // Запускаем обработку
                await StartProcessingAsync(TxtLeft.Text, TxtRight.Text);
            }
        }

        /// <summary>
        /// Основной метод обработки изображений
        /// Создает PDF файлы, затем переименовывает файлы
        /// </summary>
        /// <returns>True, если обработка прошла успешно; False, если была ошибка</returns>
        private async Task<bool> ProcessImagesAsync(string leftValue, string rightValue)
        {
            // Возвращаем результат наружу (true/false), чтобы StartProcessingAsync
            // знал, можно ли закрывать приложение
            return await Task.Run(async () =>
            {
                QuestPDF.Settings.License = LicenseType.Community;

                // Вычисляем параметры обработки
                int filesToProcess = CalculateFilesToProcess(leftValue, rightValue);

                // Проверяем достаточность файлов
                if (_imageFiles.Length < filesToProcess)
                {
                    Dispatcher.Invoke(() => ShowError($"Недостаточно файлов! Нужно: {filesToProcess}, Найдено: {_imageFiles.Length}"));
                    return false; // ошибка — сообщаем об этом наружу
                }

                Dispatcher.Invoke(() => UpdateStatus("Обработка изображений...", 0));

                // Собираем данные для PDF и список переименований
                var (pdfPages, renameList) = await CollectPdfDataAsync(leftValue, filesToProcess);

                if (pdfPages == null || renameList == null)
                    return false; // ошибка уже показана внутри CollectPdfDataAsync

                // Создаём все PDF файлы
                bool pdfSuccess = await CreatePdfFilesAsync(pdfPages, filesToProcess);

                if (!pdfSuccess)
                    return false; // ошибка уже показана внутри CreatePdfFilesAsync

                // Переименовываем файлы только если PDF созданы успешно
                await RenameFilesAsync(renameList);

                Dispatcher.Invoke(() => UpdateStatus("Готово!", 100));

                return true; // всё прошло успешно
            });
        }

        /// <summary>
        /// Вычисляет количество файлов для обработки
        /// </summary>
        private int CalculateFilesToProcess(string leftValue, string rightValue)
        {
            int leftNum = int.Parse(leftValue);
            int rightNum = int.Parse(rightValue);
            int leftDetail = leftNum % 10000;
            int rightDetail = rightNum % 10000;
            return rightDetail - leftDetail + 1;
        }

        /// <summary>
        /// Собирает данные для PDF и подготавливает список переименований
        /// </summary>
        private async Task<(List<List<(string OriginalPath, string NewNumber, byte[] ImageData)>>, List<(string OldPath, string NewPath)>)>
            CollectPdfDataAsync(string startNumber, int filesToProcess)
        {
            var pdfPages = new List<List<(string OriginalPath, string NewNumber, byte[] ImageData)>>();
            var renameList = new List<(string OldPath, string NewPath)>();

            string currentNumber = startNumber;
            int processedFiles = 0;

            for (int i = 0; i < filesToProcess; i += 5)
            {
                int filesInPage = Math.Min(5, filesToProcess - i);
                var pageFiles = new List<(string OriginalPath, string NewNumber, byte[] ImageData)>();

                for (int j = 0; j < filesInPage; j++)
                {
                    int fileIndex = i + j;
                    string filePath = _imageFiles[fileIndex];

                    // Обрабатываем изображение
                    using (var processedImage = PrepareImageForPrinting(filePath))
                    {
                        using (var ms = new MemoryStream())
                        {
                            processedImage.Save(ms, ImageFormat.Png);
                            byte[] imageData = ms.ToArray();
                            pageFiles.Add((filePath, currentNumber, imageData));
                        }
                    }

                    // Подготавливаем переименование
                    string directory = Path.GetDirectoryName(filePath);
                    string extension = Path.GetExtension(filePath);
                    string newFilePath = Path.Combine(directory, currentNumber + extension);

                    // Проверяем существование файла
                    if (CheckFileExists(newFilePath, currentNumber))
                    {
                        Dispatcher.Invoke(() => ShowError($"Остановлено: файл {currentNumber} уже существует"));
                        return (null, null);
                    }

                    renameList.Add((filePath, newFilePath));
                    currentNumber = GetNextNumber(currentNumber);

                    // Обновляем прогресс
                    processedFiles++;
                    int progress = (int)((double)processedFiles / filesToProcess * 70);
                    Dispatcher.Invoke(() => UpdateStatus($"Обработано: {processedFiles}/{filesToProcess}", progress));
                }

                pdfPages.Add(pageFiles);
            }

            return (pdfPages, renameList);
        }

        /// <summary>
        /// Создаёт все PDF файлы
        /// </summary>
        private async Task<bool> CreatePdfFilesAsync(
            List<List<(string OriginalPath, string NewNumber, byte[] ImageData)>> pdfPages,
            int filesToProcess)
        {
            string parentFolder = Directory.GetParent(Properties.Settings.Default.SelectedFolder).FullName;

            for (int pageIndex = 0; pageIndex < pdfPages.Count; pageIndex++)
            {
                var pageFiles = pdfPages[pageIndex];
                string pdfFileName = pageFiles[0].NewNumber + ".pdf";
                string pdfPath = Path.Combine(parentFolder, pdfFileName);

                int pdfProgress = 70 + (int)((double)(pageIndex + 1) / pdfPages.Count * 20);
                Dispatcher.Invoke(() => UpdateStatus($"Создание PDF {pageIndex + 1}...", pdfProgress));

                try
                {
                    // Проверяем состояние чекбокса
                    bool isPeriodic = false;
                    Dispatcher.Invoke(() => isPeriodic = ChkPeriodTests.IsChecked == true);

                    if (isPeriodic)
                        CreateSinglePdfPeriod(pdfPath, pageFiles);
                    else
                        CreateSinglePdf(pdfPath, pageFiles);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowError($"Ошибка создания PDF: {ex.Message}"));
                    return false;
                }

                await Task.Delay(50);
            }

            return true;
        }

        /// <summary>
        /// Создаёт один PDF файл ДЛЯ ПЕРЕОДИЧЕСКИХ ИСПЫТАНИЙ
        /// </summary>
        private void CreateSinglePdfPeriod(string pdfPath, List<(string OriginalPath, string NewNumber, byte[] ImageData)> pageFiles)
        {
            Document.Create(container =>
            {
                container.Page(pageDescriptor =>
                {
                    pageDescriptor.Size(PageSizes.A4);
                    pageDescriptor.Margin(20);

                    pageDescriptor.Content().Column(column =>
                    {
                        // Заголовок
                        column.Item().PaddingVertical(7);
                        column.Item().AlignCenter().Text("Скриншоты проверки ПОС на -50°C")
                            .FontSize(16).Bold();
                        column.Item().PaddingVertical(5);

                        // Создаем таблицу 2x3 для равномерного размещения
                        column.Item().PaddingLeft(40).PaddingRight(40).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            // Первый ряд
                            AddTableCell(table, pageFiles, 0);
                            AddTableCell(table, pageFiles, 1);

                            // Второй ряд
                            AddTableCell(table, pageFiles, 2);
                            AddTableCell(table, pageFiles, 3);

                            // Третий ряд
                            AddTableCell(table, pageFiles, 4);
                            table.Cell(); // Пустая ячейка справа
                        });

                        column.Item().PaddingVertical(10);

                        // Места для подписей внизу страницы
                        column.Item().PaddingTop(12).Column(signColumn =>
                        {
                            signColumn.Item().PaddingLeft(45).Text("Представитель подразделения изготовителя: _________________________________")
                                .FontSize(10);
                            signColumn.Item().PaddingLeft(45).PaddingTop(15).Text("Представитель ОТК: __________________________________________________________")
                                .FontSize(10);
                            signColumn.Item().PaddingLeft(45).PaddingTop(15).Text("Представитель ВП: ___________________________________________________________")
                                .FontSize(10);
                            signColumn.Item().PaddingLeft(45).PaddingTop(15).Text("Осциллограф TDS1002B №C059898 _______________________________________________")
                                .FontSize(10);
                            signColumn.Item().PaddingLeft(45).PaddingTop(15).Text("Камера КТХ-240 №001 _________________________________________________________")
                                .FontSize(10);
                        });
                    });
                });
            }).GeneratePdf(pdfPath);
        }

        /// <summary>
        /// Создаёт один PDF файл
        /// </summary>
        private void CreateSinglePdf(string pdfPath, List<(string OriginalPath, string NewNumber, byte[] ImageData)> pageFiles)
        {
            Document.Create(container =>
            {
                container.Page(pageDescriptor =>
                {
                    pageDescriptor.Size(PageSizes.A4);
                    pageDescriptor.Margin(20);

                    pageDescriptor.Content().Column(column =>
                    {
                        // Заголовок
                        column.Item().PaddingVertical(15);
                        column.Item().AlignCenter().Text("Скриншоты проверки ПОС на -50°C")
                            .FontSize(16).Bold();
                        column.Item().PaddingVertical(10);

                        // Создаем таблицу 2x3 для равномерного размещения
                        column.Item().PaddingLeft(40).PaddingRight(40).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            // Первый ряд
                            AddTableCell(table, pageFiles, 0);
                            AddTableCell(table, pageFiles, 1);

                            // Второй ряд
                            AddTableCell(table, pageFiles, 2);
                            AddTableCell(table, pageFiles, 3);

                            // Третий ряд
                            AddTableCell(table, pageFiles, 4);
                            table.Cell(); // Пустая ячейка справа
                        });

                        column.Item().PaddingVertical(10);

                        // Места для подписей внизу страницы
                        column.Item().PaddingTop(20).Column(signColumn =>
                        {
                            signColumn.Item().PaddingLeft(45).Text("Представитель подразделения изготовителя: _________________________________")
                                .FontSize(10);
                            signColumn.Item().PaddingLeft(45).PaddingTop(15).Text("Представитель ОТК: __________________________________________________________")
                                .FontSize(10);
                            signColumn.Item().PaddingLeft(45).PaddingTop(15).Text("Представитель ВП: ___________________________________________________________")
                                .FontSize(10);
                        });
                    });
                });
            }).GeneratePdf(pdfPath);
        }


        /// <summary>
        /// Добавляет ячейку с изображением в таблицу
        /// </summary>
        private void AddTableCell(
            QuestPDF.Fluent.TableDescriptor table,
            List<(string OriginalPath, string NewNumber, byte[] ImageData)> pageFiles,
            int index)
        {
            if (pageFiles.Count > index)
            {
                table.Cell().Padding(5).Column(col =>
                {
                    col.Item().AlignCenter().Text(pageFiles[index].NewNumber).FontSize(12).Bold();
                    col.Item().Image(pageFiles[index].ImageData).FitArea();
                });
            }
            else
            {
                table.Cell();
            }
        }

        /// <summary>
        /// Переименовывает все файлы из списка
        /// </summary>
        private async Task RenameFilesAsync(List<(string OldPath, string NewPath)> renameList)
        {
            Dispatcher.Invoke(() => UpdateStatus("Переименование файлов...", 95));

            foreach (var (oldPath, newPath) in renameList)
            {
                try
                {
                    File.Move(oldPath, newPath);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"Ошибка при переименовании файла:\n{oldPath}\n\nОшибка: {ex.Message}",
                            "Ошибка переименования",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                    return;
                }
            }
        }

    }
}