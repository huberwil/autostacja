/**
 * @file Station_Node.ino
 * @brief Oprogramowanie węzła pomiarowego (stacji polowej) z komunikacją LoRa.
 * @author Zespół Projektowy POS
 * @date 2026-05-27
 * * Program odpowiada za cykliczne wybudzanie mikrokontrolera, odczyt danych
 * z czujnika BME280 oraz sensorów mechanicznych (wiatr, deszcz), a następnie
 * transmisję spakowanego pakietu danych przez protokół LoRa.
 */

#include <SPI.h>
#include <Wire.h>
#include <LoRa.h>
#include <Adafruit_BME280.h>
#include <LowPower.h>

/// @brief Unikalny identyfikator stacji pogodowej w sieci.
const String STATION_ID = "01";

// Definicje pinów dla modułu LoRa32U4
#define LORA_NSS  8   ///< Pin wyboru układu LoRa (Chip Select)
#define LORA_RST  4   ///< Pin resetu modułu LoRa
#define LORA_DIO0 7   ///< Pin przerwania sprzętowego DIO0

// Definicje pinów dla czujników mechanicznych
#define RAIN_PIN 0    ///< Cyfrowy pin deszczomierz (Przerwanie INT0)
#define WIND_PIN 1    ///< Cyfrowy pin anemometru (Przerwanie INT1)
#define VANE_PIN A0   ///< Analogowy pin wiatrowskazu

// Globalne zmienne licznikowe modyfikowane w przerwaniach
volatile unsigned int rainTicks = 0; ///< Licznik impulsów z deszczomierza
volatile unsigned int windTicks = 0; ///< Licznik impulsów z anemometru

/// @brief Instancja obiektu czujnika BME280 działającego na magistrali I2C.
Adafruit_BME280 bme;

/**
 * @brief Procedura obsługi przerwania (ISR) dla deszczomierza.
 * * Wywoływana automatycznie przy każdym przechyleniu korytka deszczomierza.
 * Zlicza impulsy, nie blokując głównej pętli programu.
 */
void rainISR() {
    rainTicks++;
}

/**
 * @brief Procedura obsługi przerwania (ISR) dla anemometru.
 * * Wywoływana przy każdym pełnym obrocie czasz anemometru.
 */
void windISR() {
    windTicks++;
}

/**
 * @brief Funkcja inicjalizacyjna mikrokontrolera.
 * * Konfiguruje porty wejścia/wyjścia, magistralę I2C, przerwania zewnętrzne
 * oraz uruchamia moduł radiowy LoRa na częstotliwości 868 MHz.
 */
void setup() {
    // Inicjalizacja pinów przerwaniowych z podciągnięciem do VCC
    pinMode(RAIN_PIN, INPUT_PULLUP);
    pinMode(WIND_PIN, INPUT_PULLUP);
    
    // Podpięcie funkcji przerwań pod zbocze opadające (FALLING)
    attachInterrupt(digitalPinToInterrupt(RAIN_PIN), rainISR, FALLING);
    attachInterrupt(digitalPinToInterrupt(WIND_PIN), windISR, FALLING);

    // Inicjalizacja czujnika BME280
    if (!bme.begin(0x76)) {
        // Błąd krytyczny czujnika - w profesjonalnym kodzie zapętlone mruganie LED
        while (1);
    }

    // Konfiguracja sprzętowa pinów radia dla LoRa32U4
    LoRa.setPins(LORA_NSS, LORA_RST, LORA_DIO0);

    // Uruchomienie modułu radiowego na częstotliwości europejskiej 868 MHz
    if (!LoRa.begin(868E6)) {
        while (1);
    }
}

/**
 * @brief Główna pętla programu stacji polowej.
 * * Pobiera dane pomiarowe, oblicza parametry wiatru i opadów, formatuje ramkę
 * danych, wysyła pakiet radiowy LoRa, a następnie wprowadza układ w tryb głębokiego snu.
 */
void loop() {
    // 1. Odczyt danych z BME280
    float temp = bme.readTemperature();
    float hum = bme.readHumidity();
    float pres = bme.readPressure() / 100.0F; // Konwersja na hPa

    // 2. Obliczenia dla czujników impulsowych
    // Przeliczenie impulsów deszczu (1 klik = 0.28 mm opadu)
    float rainfall = rainTicks * 0.28;
    rainTicks = 0; // Zerowanie licznika po odczycie

    // Proste przeliczenie prędkości wiatru (zależne od specyfikacji czujnika)
    float windSpeed = windTicks * 0.667; 
    windTicks = 0;

    // 3. Odczyt kierunku wiatru (Analogowy ADC)
    int vaneAnalog = analogRead(VANE_PIN);

    // 4. Budowanie i formatowanie ramki danych
    // Format: ID:01;T:22.5;H:55;P:1013.2;W:4.2;D:512;R:0.56
    String dataPacket = "ID:" + STATION_ID + 
                        ";T:" + String(temp, 1) + 
                        ";H:" + String(hum, 0) + 
                        ";P:" + String(pres, 1) + 
                        ";W:" + String(windSpeed, 1) + 
                        ";D:" + String(vaneAnalog) + 
                        ";R:" + String(rainfall, 2);

    // 5. Transmisja radiowa
    LoRa.beginPacket();
    LoRa.print(dataPacket);
    LoRa.endPacket();

    // 6. Zarządzanie energią - Głębokie uśpienie (Deep Sleep)
    // Ponieważ LowPower pozwala uśpić układ na max 8 sekund, powtarzamy to w pętli
    // aby uzyskać np. ~10 minut (75 cykli * 8s = 600s). Przerwania deszczu nadal działają!
    for (int i = 0; i < 75; i++) {
        LowPower.powerDown(SLEEP_8S, ADC_OFF, BOD_OFF);
    }
}