using Xunit;
using WeatherStation; // Dostęp do Twojego kodu

namespace WeatherStation.Tests
{
    public class TemperatureTests
    {
        [Fact]
        public void Test_Czy_Temperatura_25_Jest_Poprawna()
        {
            // Arrange
            var sensor = new TemperatureSensor("TEST-1");

            // Act
            bool wynik = sensor.ValidateReading(25.0f);

            // Assert
            Assert.True(wynik);
        }

        [Fact]
        public void Test_Czy_Temperatura_Minus_50_Zostanie_Odrzucona()
        {
            // Arrange
            var sensor = new TemperatureSensor("TEST-1");

            // Act
            bool wynik = sensor.ValidateReading(-50.0f); // Limit to -40

            // Assert
            Assert.False(wynik); // Oczekujemy false, bo to poza zakresem
        }
        [Fact]
        public void Test_Czy_Srednia_Dla_Braku_Danych_Zwraca_Zero_Bez_Awarii()
        {
            // Arrange (Przygotowanie stacji bez generowania danych)
            var weatherData = new WeatherData("STACJA-TESTOWA");

            // Act (Próba policzenia średniej z ostatnich 7 dni)
            float wynik = weatherData.GetAverageTemperatureLastDays(7);

            // Assert (Oczekujemy, że zwróci 0.0f zamiast wywalić program)
            Assert.Equal(0.0f, wynik);
        }
    }

}
