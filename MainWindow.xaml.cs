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

                // Основная логика обработки изображений
                await ProcessImagesAsync(leftValue, rightValue);

                UpdateStatus("Обработка завершена", 100);

                // Небольшая задержка чтобы пользователь увидел "Готово!"
                await Task.Delay(500);

                // Закрываем приложение
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

        /// <summary>
        /// Проверяет, содержит ли папка только переименованные файлы (без TEK*.JPG)
        /// </summary>
        private bool IsFolderProcessed(string folderPath)
        {
            try
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp" };
                var imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                    .ToArray();

                if (imageFiles.Length == 0)
                    return true;

                bool hasTekFiles = imageFiles.Any(f =>
                    Path.GetFileName(f).StartsWith("TEK", StringComparison.OrdinalIgnoreCase));

                return !hasTekFiles;
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

                foreach (var yearFolder in yearFolders)
                {
                    string yearName = Path.GetFileName(yearFolder);

                    if (!int.TryParse(yearName, out int year))
                        continue;

                    if (_excludedYears.Contains(year))
                    {
                        continue;
                    }

                    if (IsFolderProcessed(yearFolder))
                    {
                        _excludedYears.Add(year);
                        excludedListChanged = true;
                        continue;
                    }

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

                    allFiles.AddRange(files);
                }

                if (excludedListChanged)
                {
                    Dispatcher.Invoke(() => SaveExcludedYears());
                }

                // Сортируем файлы по пути (год/дата/имя файла)
                _imageFiles = allFiles
                    .OrderBy(f => f) // Сортировка по полному пути
                    .ToArray();
            });

            _isScanning = false;
            UpdateStatus($"Найдено файлов: {_imageFiles.Length}", 0);
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
            // Разбираем номер: ГГ ПП ДДДДД
            int year = int.Parse(currentNumber.Substring(0, 2));
            int batch = int.Parse(currentNumber.Substring(2, 2));
            int detail = int.Parse(currentNumber.Substring(4, 4));

            // Увеличиваем номер детали
            detail++;

            // Проверяем последнюю цифру
            int lastDigit = detail % 10;

            // Если последняя цифра стала 1 (новая партия началась)
            if (lastDigit == 1 && detail > int.Parse(currentNumber.Substring(4, 4)))
            {
                batch++;
            }

            // Формируем новый номер
            return $"{year:D2}{batch:D2}{detail:D4}";
        }

        /// <summary>
        /// Конвертирует изображение осциллографа для ч/б печати
        /// </summary>
        private Bitmap PrepareImageForPrinting(string imagePath)
        {
            using (var original = new Bitmap(imagePath))
            {
                // Проверяем левый нижний угол
                DrawingColor checkPixel = original.GetPixel(10, original.Height - 10);

                // Если яркость >= 50%, изображение уже готово
                if (checkPixel.GetBrightness() >= 0.5f)
                {
                    return new Bitmap(original);
                }

                // Обрабатываем изображение (конвертация цветов)
                var result = new Bitmap(original.Width, original.Height);

                for (int y = 0; y < original.Height; y++)
                {
                    for (int x = 0; x < original.Width; x++)
                    {
                        DrawingColor pixel = original.GetPixel(x, y);

                        // Темный фон (черный/синий) -> белый
                        if (pixel.GetBrightness() < 0.3f)
                        {
                            result.SetPixel(x, y, DrawingColor.White);
                        }
                        // Яркий (желтый график, белый текст) -> черный
                        else if (pixel.GetBrightness() > 0.5f)
                        {
                            result.SetPixel(x, y, DrawingColor.Black);
                        }
                        // Промежуточные (сетка) -> светло-серый
                        else
                        {
                            result.SetPixel(x, y, DrawingColor.LightGray);
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Основной метод обработки изображений
        /// Переименовывает файлы, создает отдельные PDF файлы с 5 изображениями
        /// </summary>
        private async Task ProcessImagesAsync(string leftValue, string rightValue)
        {
            await Task.Run(async () =>
            {
                // Настройка QuestPDF лицензии (Community)
                QuestPDF.Settings.License = LicenseType.Community;

                string currentNumber = leftValue;

                // Вычисляем сколько файлов нужно обработать
                int leftNum = int.Parse(leftValue);
                int rightNum = int.Parse(rightValue);
                int leftDetail = leftNum % 10000;
                int rightDetail = rightNum % 10000;
                int filesToProcess = rightDetail - leftDetail + 1;

                // Проверяем что файлов достаточно
                if (_imageFiles.Length < filesToProcess)
                {
                    Dispatcher.Invoke(() => ShowError($"Недостаточно файлов! Нужно: {filesToProcess}, Найдено: {_imageFiles.Length}"));
                    return;
                }

                int processedFiles = 0;

                Dispatcher.Invoke(() => UpdateStatus("Обработка изображений...", 0));

                // Обрабатываем только нужное количество файлов
                for (int i = 0; i < filesToProcess; i += 5)
                {
                    // Определяем сколько файлов взять для этой страницы (максимум 5)
                    int filesInPage = Math.Min(5, filesToProcess - i);
                    var pageFiles = new List<(string OriginalPath, string NewNumber, byte[] ImageData)>();

                    // Собираем файлы для одной страницы
                    for (int j = 0; j < filesInPage; j++)
                    {
                        int fileIndex = i + j;
                        string filePath = _imageFiles[fileIndex];

                        // Подготавливаем изображение для печати
                        using (var processedImage = PrepareImageForPrinting(filePath))
                        {
                            // Конвертируем в байты для PDF
                            using (var ms = new MemoryStream())
                            {
                                processedImage.Save(ms, ImageFormat.Png);
                                byte[] imageData = ms.ToArray();
                                pageFiles.Add((filePath, currentNumber, imageData));
                            }
                        }

                        // Переименовываем файл на диске
                        string directory = Path.GetDirectoryName(filePath);
                        string extension = Path.GetExtension(filePath);
                        string newFilePath = Path.Combine(directory, currentNumber + extension);

                        try
                        {
                            File.Move(filePath, newFilePath);
                        }
                        catch
                        {
                            // Если файл уже существует или ошибка - пропускаем
                        }

                        // Генерируем следующий номер
                        currentNumber = GetNextNumber(currentNumber);

                        // Обновляем прогресс
                        processedFiles++;
                        int progress = (int)((double)processedFiles / filesToProcess * 80);
                        Dispatcher.Invoke(() => UpdateStatus($"Обработано: {processedFiles}/{filesToProcess}", progress));
                    }

                    // Создаем отдельный PDF файл для этой страницы
                    string pdfFileName = pageFiles[0].NewNumber + ".pdf";
                    string pdfPath = Path.Combine(Properties.Settings.Default.SelectedFolder, pdfFileName);

                    int pdfProgress = 80 + (int)((double)(i / 5 + 1) / Math.Ceiling(filesToProcess / 5.0) * 15);
                    Dispatcher.Invoke(() => UpdateStatus($"Создание PDF {i / 5 + 1}...", pdfProgress));

                    // Создаем PDF с одной страницей
                    Document.Create(container =>
                    {
                        container.Page(pageDescriptor =>
                        {
                            pageDescriptor.Size(PageSizes.A4);
                            pageDescriptor.Margin(20);

                            pageDescriptor.Content().Column(column =>
                            {
                                // Заголовок
                                column.Item().AlignCenter().Text("Скриншоты проверки ПОС на -50°C")
                                    .FontSize(16).Bold();

                                column.Item().PaddingVertical(10);

                                // Создаем таблицу 2x3 для равномерного размещения
                                column.Item().Table(table =>
                                {
                                    // Определяем 2 колонки равной ширины
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn();
                                        columns.RelativeColumn();
                                    });

                                    // Первый ряд (2 картинки)
                                    if (pageFiles.Count >= 1)
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC").Padding(5).Column(col =>
                                        {
                                            col.Item().AlignCenter().Text(pageFiles[0].NewNumber).FontSize(12).Bold();
                                            col.Item().Height(150).Image(pageFiles[0].ImageData);
                                        });
                                    }
                                    else
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC");
                                    }

                                    if (pageFiles.Count >= 2)
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC").Padding(5).Column(col =>
                                        {
                                            col.Item().AlignCenter().Text(pageFiles[1].NewNumber).FontSize(12).Bold();
                                            col.Item().Height(150).Image(pageFiles[1].ImageData);
                                        });
                                    }
                                    else
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC");
                                    }

                                    // Второй ряд (2 картинки)
                                    if (pageFiles.Count >= 3)
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC").Padding(5).Column(col =>
                                        {
                                            col.Item().AlignCenter().Text(pageFiles[2].NewNumber).FontSize(12).Bold();
                                            col.Item().Height(150).Image(pageFiles[2].ImageData);
                                        });
                                    }
                                    else
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC");
                                    }

                                    if (pageFiles.Count >= 4)
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC").Padding(5).Column(col =>
                                        {
                                            col.Item().AlignCenter().Text(pageFiles[3].NewNumber).FontSize(12).Bold();
                                            col.Item().Height(150).Image(pageFiles[3].ImageData);
                                        });
                                    }
                                    else
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC");
                                    }

                                    // Третий ряд (1 картинка слева)
                                    if (pageFiles.Count >= 5)
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC").Padding(5).Column(col =>
                                        {
                                            col.Item().AlignCenter().Text(pageFiles[4].NewNumber).FontSize(12).Bold();
                                            col.Item().Height(150).Image(pageFiles[4].ImageData);
                                        });
                                    }
                                    else
                                    {
                                        table.Cell().Border(1).BorderColor("#CCCCCC");
                                    }

                                    // Пустая ячейка справа
                                    table.Cell().Border(1).BorderColor("#CCCCCC");
                                });

                                column.Item().PaddingVertical(10);

                                // Места для подписей внизу страницы
                                column.Item().PaddingTop(20).Column(signColumn =>
                                {
                                    signColumn.Item().Text("Представитель подразделения изготовителя: _________________________________")
                                        .FontSize(10);
                                    signColumn.Item().PaddingTop(10).Text("Представитель ОТК: _________________________________")
                                        .FontSize(10);
                                    signColumn.Item().PaddingTop(10).Text("Представитель ВП: __________________________________")
                                        .FontSize(10);
                                });
                            });
                        });
                    }).GeneratePdf(pdfPath);

                    // Небольшая пауза между созданием PDF файлов
                    await Task.Delay(50);
                }

                Dispatcher.Invoke(() => UpdateStatus("Готово!", 100));
            });
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
    }
}