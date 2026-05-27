/**
 * @file Base_Gateway.ino
 * @brief Oprogramowanie stacji bazowej (odbiornika) z komunikacją LoRa i Serial-USB.
 * @author Zespół Projektowy POS
 * @date 2026-05-27
 * * Program działa w trybie ciągłego nasłuchu radiowego. Po odebraniu pakietu
 * od stacji polowej, weryfikuje jego poprawność i przesyła go przez interfejs
 * szeregowy USB bezpośrednio do nadrzędnej aplikacji C# (.NET).
 */

#include <SPI.h>
#include <LoRa.h>

// Definicje pinów dla modułu LoRa32U4 (identyczne jak w nadajniku)
#define LORA_NSS  8   ///< Pin wyboru układu LoRa (Chip Select)
#define LORA_RST  4   ///< Pin resetu modułu LoRa
#define LORA_DIO0 7   ///< Pin przerwania sprzętowego DIO0

/**
 * @brief Funkcja inicjalizacyjna stacji bazowej.
 * * Konfiguruje komunikację Serial-USB z komputerem oraz uruchamia moduł
 * radiowy LoRa w trybie nasłuchiwania.
 */
void setup() {
    // Inicjalizacja portu szeregowego USB z prędkością 9600 bodów
    Serial.begin(9600);
    while (!Serial); // Oczekiwanie na otwarcie portu przez aplikację C#

    // Konfiguracja sprzętowa pinów radia dla LoRa32U4
    LoRa.setPins(LORA_NSS, LORA_RST, LORA_DIO0);

    // Uruchomienie modułu radiowego na częstotliwości 868 MHz
    if (!LoRa.begin(868E6)) {
        Serial.println("BLAD: Nie udalo sie uruchomic modulu LoRa odbiornika.");
        while (1);
    }
}

/**
 * @brief Główna pętla programu stacji bazowej.
 * * Stale sprawdza bufor odbiornika radiowego. W przypadku wykrycia transmisji,
 * pobiera zawartość paczki i przekazuje ją za pomocą Serial.println() do PC.
 */
void loop() {
    // Sprawdzenie, czy w powietrzu pojawił się pakiet danych (zwraca rozmiar pakietu w bajtach)
    int packetSize = LoRa.parsePacket();
    
    if (packetSize) {
        String receivedData = "";

        // Odczyt zawartości pakietu znak po znaku
        while (LoRa.available()) {
            receivedData += (char)LoRa.read();
        }

        // Pobranie parametrów siły sygnału RSSI dla diagnostyki sieci
        int rssi = LoRa.packetRssi();

        // Dodanie informacji o sile sygnału do ramki danych dla aplikacji C#
        // Wynikowy format na USB: ID:01;T:22.5;...;R:0.56;RSSI:-75
        String finalOutput = receivedData + ";RSSI:" + String(rssi);

        // Przesłanie kompletnych danych kablem USB do aplikacji C#
        Serial.println(finalOutput);
    }
}