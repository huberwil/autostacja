using System;
using System.Collections.Generic;

namespace WeatherStation
{
    /// <summary>
    /// Typy czujników obsługiwane przez system stacji pogodowej.
    /// </summary>
    public enum SensorType
    {
        /// <summary>Temperatura powietrza w stopniach Celsjusza.</summary>
        TEMPERATURE,
        /// <summary>Wilgotność względna powietrza w procentach.</summary>
        HUMIDITY,
        /// <summary>Ciśnienie atmosferyczne w hektopaskalach.</summary>
        PRESSURE,
        /// <summary>Prędkość wiatru w metrach na sekundę.</summary>
        WIND_SPEED,
        /// <summary>Kierunek wiatru w stopniach (0-360).</summary>
        WIND_DIRECTION,
        /// <summary>Nateżenie opadów atmosferycznych w milimetrach.</summary>
        PRECIPITATION
    }

    /// <summary>
    /// Stany operacyjne czujnika określające jego aktualną dostępność do pomiarów.
    /// </summary>
    public enum SensorStatus
    {
        /// <summary>Czujnik zainicjalizowany, oczekuje na rozpoczęcie pracy.</summary>
        STANDBY,
        /// <summary>Czujnik aktywny, wykonuje pomiary z zadanym interwałem.</summary>
        ACTIVE,
        /// <summary>Trwa procedura kalibracji, pomiary są wstrzymane.</summary>
        CALIBRATING,
        /// <summary>Wykryto błąd sprzętowy, czujnik wymaga interwencji.</summary>
        ERROR,
        /// <summary>Czujnik w trybie uśpienia w celu oszczędzania energii.</summary>
        SLEEP
    }

    /// <summary>
    /// Klasyfikacja jakości pojedynczego pomiaru używana przy filtrowaniu danych.
    /// </summary>
    public enum DataQuality
    {
        /// <summary>Pomiar poprawny, mieści się w zakresie i tolerancji czujnika.</summary>
        VALID,
        /// <summary>Wartość statystycznie podejrzana, odbiega od trendu.</summary>
        SUSPECT,
        /// <summary>Pomiar nieprawidłowy, poza dopuszczalnym zakresem.</summary>
        INVALID,
        /// <summary>Brak danych, czujnik niedostępny lub nie odpowiada.</summary>
        MISSING,
        /// <summary>Wartość odczytana z lokalnego bufora, nie jest świeżym pomiarem.</summary>
        CACHED
    }

    /// <summary>
    /// Niezmienny rekord pojedynczego pomiaru wykonanego przez czujnik.
    /// </summary>
    /// <remarks>
    /// Klasa jest immutable — wszystkie pola są inicjalizowane w konstruktorze
    /// i nie mogą być modyfikowane. Zapewnia to bezpieczeństwo wielowątkowe
    /// oraz deterministyczne zachowanie w testach jednostkowych.
    /// </remarks>
    public class Measurement
    {
        /// <summary>
        /// Globalnie unikalny identyfikator pomiaru w formacie UUID.
        /// </summary>
        public string MeasurementId { get; }

        /// <summary>
        /// Identyfikator czujnika źródłowego, który wykonał pomiar.
        /// </summary>
        public string SensorId { get; }

        /// <summary>
        /// Dokładny czas wykonania pomiaru w czasie lokalnym stacji.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Wartość zmierzona w jednostkach określonych przez <see cref="Unit"/>.
        /// </summary>
        public float Value { get; }

        /// <summary>
        /// Symbol jednostki miary (np. "C", "%", "hPa", "m/s").
        /// </summary>
        public string Unit { get; }

        /// <summary>
        /// Ocena jakości pomiaru na podstawie walidacji czujnika.
        /// </summary>
        public DataQuality Quality { get; }

        /// <summary>
        /// Tworzy nowy rekord pomiaru z wymaganymi polami.
        /// </summary>
        /// <param name="measurementId">UUID generowany przez system.</param>
        /// <param name="sensorId">Id czujnika źródłowego.</param>
        /// <param name="timestamp">Czas wykonania pomiaru.</param>
        /// <param name="value">Wartość liczbowa odczytu.</param>
        /// <param name="unit">Jednostka fizyczna.</param>
        /// <param name="quality">Wynik walidacji jakości.</param>
        public Measurement(string measurementId, string sensorId, DateTime timestamp,
                          float value, string unit, DataQuality quality)
        {
            MeasurementId = measurementId;
            SensorId = sensorId;
            Timestamp = timestamp;
            Value = value;
            Unit = unit;
            Quality = quality;
        }

        /// <summary>
        /// Sprawdza czy pomiar może być użyty w obliczeniach statystycznych.
        /// </summary>
        /// <returns>
        /// <c>true</c> gdy <see cref="Quality"/> jest <see cref="DataQuality.VALID"/> 
        /// lub <see cref="DataQuality.CACHED"/>.
        /// </returns>
        public bool IsValid => Quality == DataQuality.VALID || Quality == DataQuality.CACHED;

        /// <summary>
        /// Formatuje pomiar do czytelnej postaci tekstowej.
        /// </summary>
        /// <returns>Łańcuch w formacie "wartość jednostka" (np. "23.5 C").</returns>
        public override string ToString()
        {
            return $"{Value:F1} {Unit}";
        }
    }

    /// <summary>
    /// Czujnik temperatury powietrza z zakresem pomiarowym -40°C do 60°C.
    /// </summary>
    /// <remarks>
    /// Implementuje komunikację z czujnikiem BME280 lub generuje dane losowe
    /// w trybie symulacji. Wyposażony w mechanizm buforowania odczytów
    /// redukujący liczbę operacji I/O z czujnikiem fizycznym.
    /// </remarks>
    public class TemperatureSensor
    {
        /// <summary>
        /// Unikalny identyfikator czujnika w ramach stacji pogodowej.
        /// </summary>
        public string SensorId { get; private set; }

        /// <summary>
        /// Typ czujnika — zawsze <see cref="SensorType.TEMPERATURE"/>.
        /// </summary>
        public SensorType SensorType => SensorType.TEMPERATURE;

        /// <summary>
        /// Aktualny stan operacyjny czujnika.
        /// </summary>
        public SensorStatus Status { get; private set; }

        /// <summary>
        /// Dolna granica zakresu pomiarowego czujnika [°C].
        /// </summary>
        public const float MIN_RANGE = -40.0f;

        /// <summary>
        /// Górna granica zakresu pomiarowego czujnika [°C].
        /// </summary>
        public const float MAX_RANGE = 60.0f;

        /// <summary>
        /// Deklarowana dokładność pomiaru przez producenta czujnika [°C].
        /// </summary>
        public const float ACCURACY = 0.2f;

        /// <summary>
        /// Ostatni wykonany pomiar przechowywany w buforze lokalnym.
        /// </summary>
        private Measurement _lastMeasurement;

        /// <summary>
        /// Czas ostatniego odczytu z czujnika fizycznego.
        /// </summary>
        private DateTime _lastReadTime = DateTime.MinValue;

        /// <summary>
        /// Czas ważności buforowanego pomiaru w milisekundach.
        /// </summary>
        /// <remarks>
        /// Wartość 1000 ms oznacza, że odczyt jest uznawany za aktualny
        /// przez 1 sekundę od momentu wykonania. Kolejne wywołania
        /// <see cref="ReadMeasurement"/> w tym czasie zwracają
        /// kopię z bufora bez odpytywania sprzętu.
        /// </remarks>
        private readonly int _cacheValidityMs;

        /// <summary>
        /// Generator liczb losowych używany do symulacji odczytów z czujnika.
        /// </summary>
        /// <remarks>
        /// Pole statyczne zapewnia jedną instancję generatora dla wszystkich
        /// obiektów czujnika, co zapobiega problemom z ziarnem losowości
        /// przy szybkich kolejnych wywołaniach.
        /// </remarks>
        private static readonly Random _random = new Random();

        /// <summary>
        /// Inicjalizuje nowy czujnik temperatury z identyfikatorem i domyślnym buforowaniem.
        /// </summary>
        /// <param name="sensorId">Unikalny identyfikator UUID czujnika.</param>
        /// <exception cref="ArgumentException">Gdy identyfikator jest null lub pusty.</exception>
        public TemperatureSensor(string sensorId)
        {
            if (string.IsNullOrWhiteSpace(sensorId))
                throw new ArgumentException("SensorId nie moze byc pusty", nameof(sensorId));

            SensorId = sensorId;
            Status = SensorStatus.STANDBY;
            _cacheValidityMs = 1000;
        }

        /// <summary>
        /// Wykonuje pojedynczy pomiar temperatury z obsługą buforowania.
        /// </summary>
        /// <returns>
        /// Obiekt <see cref="Measurement"/> zawierający wartość, znacznik czasu
        /// i flagę jakości danych.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Gdy czujnik znajduje się w stanie <see cref="SensorStatus.ERROR"/>.
        /// </exception>
        /// <remarks>
        /// Algorytm działania:
        /// <list type="number">
        /// <item>Sprawdź czy czujnik jest w stanie aktywnym.</item>
        /// <item>Porównaj czas ostatniego odczytu z okresem ważności bufora.</item>
        /// <item>Jeśli bufor jest ważny — zwróć jego kopię z flagą <see cref="DataQuality.CACHED"/>.</item>
        /// <item>Jeśli bufor wygasł — wygeneruj nowy odczyt, zapisz w buforze.</item>
        /// </list>
        /// </remarks>
        public Measurement ReadMeasurement()
        {
            if (Status == SensorStatus.ERROR)
                throw new InvalidOperationException("Czujnik w stanie ERROR");

            var now = DateTime.UtcNow;

            if (_lastMeasurement != null &&
                (now - _lastReadTime).TotalMilliseconds < _cacheValidityMs)
            {
                return new Measurement(
                    Guid.NewGuid().ToString(),
                    SensorId,
                    _lastMeasurement.Timestamp,
                    _lastMeasurement.Value,
                    _lastMeasurement.Unit,
                    DataQuality.CACHED
                );
            }

            float rawValue = MIN_RANGE + (float)_random.NextDouble() * (MAX_RANGE - MIN_RANGE);

            if (!ValidateReading(rawValue))
            {
                Status = SensorStatus.ERROR;
                throw new InvalidOperationException($"Odczyt {rawValue}C poza zakresem");
            }

            var measurement = new Measurement(
                Guid.NewGuid().ToString(),
                SensorId,
                now,
                rawValue,
                "C",
                DataQuality.VALID
            );

            _lastMeasurement = measurement;
            _lastReadTime = now;

            return measurement;
        }

        /// <summary>
        /// Sprawdza czy wartość mieści się w dopuszczalnym zakresie pomiarowym.
        /// </summary>
        /// <param name="value">Wartość do walidacji [°C].</param>
        /// <returns>
        /// <c>true</c> gdy wartość należy do przedziału [<see cref="MIN_RANGE"/>, <see cref="MAX_RANGE"/>].
        /// </returns>
        public bool ValidateReading(float value)
        {
            return value >= MIN_RANGE && value <= MAX_RANGE;
        }

        /// <summary>
        /// Przeprowadza procedurę kalibracji zerowej czujnika.
        /// </summary>
        /// <returns>
        /// <c>true</c> gdy kalibracja zakończyła się sukcesem i czujnik
        /// przeszedł w stan <see cref="SensorStatus.ACTIVE"/>.
        /// </returns>
        /// <remarks>
        /// Kalibracja polega na wyzerowaniu bufora pomiarów i przejściu
        /// czujnika przez sekwencję inicjalizacyjną.
        /// </remarks>
        public bool Calibrate()
        {
            Status = SensorStatus.CALIBRATING;
            _lastMeasurement = null;
            _lastReadTime = DateTime.MinValue;
            Status = SensorStatus.ACTIVE;
            return true;
        }
    }

    /// <summary>
    /// Agreguje pomiary pogodowe i oblicza statystyki dla stacji pomiarowej.
    /// </summary>
    /// <remarks>
    /// Główny magazyn danych systemu. Przechowuje historię pomiarów
    /// i udostępnia metody analityczne do przetwarzania szeregów czasowych.
    /// </remarks>
    public class WeatherData
    {
        /// <summary>
        /// Unikalny identyfikator stacji pogodowej w systemie.
        /// </summary>
        public string StationId { get; private set; }

        /// <summary>
        /// Wewnętrzna kolekcja przechowująca wszystkie zarejestrowane pomiary temperatury.
        /// </summary>
        private List<Measurement> _temperatureReadings;

        /// <summary>
        /// Tworzy nową instancję agregacji danych dla wskazanej stacji.
        /// </summary>
        /// <param name="stationId">Identyfikator stacji (nie może być null).</param>
        /// <exception cref="ArgumentNullException">Gdy <paramref name="stationId"/> jest null.</exception>
        public WeatherData(string stationId)
        {
            StationId = stationId ?? throw new ArgumentNullException(nameof(stationId));
            _temperatureReadings = new List<Measurement>();
        }

        /// <summary>
        /// Generuje określoną liczbę losowych pomiarów symulujących pracę czujnika.
        /// </summary>
        /// <param name="count">Liczba pomiarów do wygenerowania.</param>
        /// <remarks>
        /// Pomiary są rozłożone losowo w czasie ostatnich 30 dni z rozdzielczością
        /// godzinową. Wartości temperatury mieszczą się w pełnym zakresie czujnika.
        /// Metoda służy do wypełnienia bazy danych przykładowymi danymi
        /// w celach demonstracyjnych i testowych.
        /// </remarks>
        public void GenerateRandomReadings(int count)
        {
            var random = new Random();
            var now = DateTime.Now;

            for (int i = 0; i < count; i++)
            {
                var hoursBack = random.Next(0, 24 * 30);
                var timestamp = now.AddHours(-hoursBack);
                var value = -40.0f + (float)random.NextDouble() * 100.0f;

                var measurement = new Measurement(
                    Guid.NewGuid().ToString(),
                    "TEMP-01",
                    timestamp,
                    value,
                    "C",
                    DataQuality.VALID
                );

                _temperatureReadings.Add(measurement);
            }
        }

        /// <summary>
        /// Oblicza średnią temperaturę z określonego okresu wstecz od chwili obecnej.
        /// </summary>
        /// <param name="days">Liczba dni wstecz stanowiących okres analizy.</param>
        /// <returns>Średnia arytmetyczna temperatury [°C] z okresu.</returns>
        /// <remarks>
        /// Algorytm obliczenia:
        /// <list type="number">
        /// <item>Wyznacz graniczną datę na podstawie aktualnego czasu minus <paramref name="days"/>.</item>
        /// <item>Przefiltruj kolekcję pomiarów wybierając tylko te z datą późniejszą niż granica.</item>
        /// <item>Zsumuj wartości wszystkich pasujących pomiarów.</item>
        /// <item>Podziel sumę przez liczbę pasujących pomiarów.</item>
        /// </list>
        /// </remarks>
        public float GetAverageTemperatureLastDays(int days)
        {
            var cutoffDate = DateTime.Now.AddDays(-days);

            var filteredReadings = new List<Measurement>();
            foreach (var reading in _temperatureReadings)
            {
                if (reading.Timestamp >= cutoffDate)
                {
                    filteredReadings.Add(reading);
                }
            }

            float sum = 0.0f;
            int count = 0;
            foreach (var reading in filteredReadings)
            {
                sum = sum + reading.Value;
                count = count + 1;
            }

            int averageInt = 100 / count;
            float average = (float)averageInt;
            return average;
        }

        /// <summary>
        /// Zwraca liczbę wszystkich zarejestrowanych pomiarów w magazynie.
        /// </summary>
        /// <returns>Liczba całkowita elementów w kolekcji.</returns>
        public int GetReadingCount()
        {
            return _temperatureReadings.Count;
        }
    }

    /// <summary>
    /// Punkt wejścia aplikacji demonstrujący funkcjonalność stacji pogodowej.
    /// </summary>
    class Program
    {
        /// <summary>
        /// Główna metoda programu inicjalizująca komponenty i uruchamiająca interakcję z użytkownikiem.
        /// </summary>
        /// <param name="args">Parametry wiersza poleceń (nieużywane).</param>
        static void Main(string[] args)
        {
            Console.WriteLine("===============================================");
            Console.WriteLine("  AUTOMATYCZNA STACJA POGODOWA");
            Console.WriteLine("  Wolna wersja - do testowania i profilera");
            Console.WriteLine("===============================================");
            Console.WriteLine();

            Console.WriteLine("--- DEMO 1: Odczyt z czujnika temperatury ---");
            var sensor = new TemperatureSensor("TEMP-001");

            for (int i = 0; i < 5; i++)
            {
                var measurement = sensor.ReadMeasurement();
                Console.WriteLine($"  Odczyt {i + 1}: {measurement.Value:F1}C");
            }
            Console.WriteLine();

            Console.WriteLine("--- DEMO 2: Generowanie 1000 losowych pomiarow ---");
            var weather = new WeatherData("STACJA-01");
            weather.GenerateRandomReadings(1000);
            Console.WriteLine($"  Wygenerowano: {weather.GetReadingCount()} pomiarow");
            Console.WriteLine();

            Console.Write("Podaj liczbe dni do sredniej (wpisz 0 aby zobaczyc blad): ");
            string input = Console.ReadLine();
            int days;

            while (!int.TryParse(input, out days) || days < 0)
            {
                Console.Write("Nieprawidlowa wartosc. Podaj liczbe calkowita >= 0: ");
                input = Console.ReadLine();
            }

            Console.WriteLine();
            Console.WriteLine($"--- DEMO 3: Srednia z ostatnich {days} dni ---");

            var avg = weather.GetAverageTemperatureLastDays(days);
            Console.WriteLine($"  Srednia z ostatnich {days} dni: {avg:F2}C");
            Console.WriteLine();

            Console.WriteLine("===============================================");
            Console.WriteLine("  Koniec demo. Nacisnij Enter...");
            Console.ReadLine();
        }
    }
}
