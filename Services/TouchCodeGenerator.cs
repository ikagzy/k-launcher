using System.Text;

namespace KLauncher.Services;

public enum TouchBoard
{
    Esp32,
    Esp32S3,
    Esp32C3,
    Esp8266,
    RaspberryPiPico,
    ArduinoAvr
}

public enum TouchChip
{
    Xpt2046,
    Ft6206,
    Gt911,
    Cst816s
}

public enum TouchOutput
{
    SerialTest,
    TftEspiDemo,
    LvglDriver
}

/// <summary>Everything the generated sketch depends on. Pins set to -1 are not connected.</summary>
public sealed record TouchConfig(
    TouchBoard Board,
    TouchChip Chip,
    TouchOutput Output,
    int Width,
    int Height,
    int Rotation,
    int Sck,
    int Miso,
    int Mosi,
    int Cs,
    int Irq,
    int Sda,
    int Scl,
    int Int,
    int Rst,
    bool SeparateSpiBus,
    bool SwapXY,
    bool InvertX,
    bool InvertY,
    int RawMinX = 200,
    int RawMaxX = 3700,
    int RawMinY = 240,
    int RawMaxY = 3800)
{
    public static bool UsesSpi(TouchChip chip) => chip == TouchChip.Xpt2046;
    public static bool IsEsp32Family(TouchBoard board) => board is TouchBoard.Esp32 or TouchBoard.Esp32S3 or TouchBoard.Esp32C3;
}

/// <summary>A ready-made wiring for a board people commonly buy as one piece.</summary>
public sealed record TouchPreset(string Key, string Name, TouchConfig Config);

/// <summary>
/// Writes an Arduino sketch that adds touch input for small displays on
/// ESP32-style boards. The sketch has one touchBegin()/readTouch() pair per
/// touch chip, and the chosen output (serial test, TFT_eSPI demo or LVGL
/// input driver) only talks to readTouch(), so every combination fits.
/// </summary>
public static class TouchCodeGenerator
{
    public static IReadOnlyList<TouchPreset> Presets { get; } =
    [
        new("CustomEsp32", "ESP32 + ILI9341 (XPT2046)",
            new(TouchBoard.Esp32, TouchChip.Xpt2046, TouchOutput.SerialTest, 320, 240, 1,
                18, 19, 23, 21, -1, 21, 22, -1, -1, false, false, false, false)),
        new("Cyd", "Cheap Yellow Display (ESP32-2432S028R)",
            new(TouchBoard.Esp32, TouchChip.Xpt2046, TouchOutput.SerialTest, 320, 240, 1,
                25, 39, 32, 33, 36, 21, 22, -1, -1, true, false, false, false)),
        new("AdafruitCap", "Adafruit 2.8\" (FT6206)",
            new(TouchBoard.Esp32, TouchChip.Ft6206, TouchOutput.SerialTest, 320, 240, 1,
                18, 19, 23, 5, -1, 21, 22, -1, -1, false, true, false, true)),
        new("Sunton43", "ESP32-S3 4.3\" 800×480 (GT911)",
            new(TouchBoard.Esp32S3, TouchChip.Gt911, TouchOutput.SerialTest, 800, 480, 0,
                12, 13, 11, 10, -1, 19, 20, -1, 38, false, false, false, false)),
        new("Round128", "ESP32-C3 1.28\" (CST816S)",
            new(TouchBoard.Esp32C3, TouchChip.Cst816s, TouchOutput.SerialTest, 240, 240, 0,
                6, -1, 7, 10, 0, 4, 5, -1, 1, false, false, false, false)),
        new("Pico", "Raspberry Pi Pico + XPT2046",
            new(TouchBoard.RaspberryPiPico, TouchChip.Xpt2046, TouchOutput.SerialTest, 320, 240, 1,
                18, 16, 19, 17, -1, 4, 5, -1, -1, false, false, false, false))
    ];

    /// <summary>Library Manager names, in install order.</summary>
    public static IReadOnlyList<string> RequiredLibraries(TouchConfig config)
    {
        var libraries = new List<string>
        {
            config.Chip switch
            {
                TouchChip.Ft6206 => "Adafruit FT6206 Library",
                TouchChip.Gt911 => "TAMC_GT911",
                TouchChip.Cst816s => "CST816S (fbiego)",
                _ => "XPT2046_Touchscreen (Paul Stoffregen)"
            }
        };
        if (config.Output is TouchOutput.TftEspiDemo or TouchOutput.LvglDriver)
            libraries.Add("TFT_eSPI (Bodmer)");
        if (config.Output == TouchOutput.LvglDriver)
            libraries.Add("lvgl (9.x)");
        return libraries;
    }

    public static string SketchName(TouchConfig config) =>
        $"KTouch_{config.Chip}_{config.Board}";

    // ------------------------------------------------------------ Chip finder

    private static (int Sda, int Scl)[] FinderI2cPins(TouchBoard board) => board switch
    {
        TouchBoard.Esp32 => [(21, 22), (33, 32), (26, 27)],
        // 19/20 are the native USB pins on the S3; they are tried last and only
        // when Serial does not run over that USB port.
        TouchBoard.Esp32S3 => [(8, 9), (17, 18), (4, 5), (15, 16), (19, 20)],
        TouchBoard.Esp32C3 => [(4, 5), (8, 9), (6, 7)],
        TouchBoard.Esp8266 => [(4, 5)],
        TouchBoard.RaspberryPiPico => [(4, 5), (0, 1), (20, 21)],
        _ => [(-1, -1)]
    };

    private static (int Sck, int Miso, int Mosi, int Cs)[] FinderSpiPins(TouchBoard board) => board switch
    {
        TouchBoard.Esp32 => [(25, 39, 32, 33), (18, 19, 23, 21), (18, 19, 23, 5), (14, 12, 13, 33)],
        TouchBoard.Esp32S3 => [(12, 13, 11, 10), (36, 37, 35, 38)],
        TouchBoard.Esp32C3 => [(4, 5, 6, 7), (6, 2, 7, 10)],
        TouchBoard.Esp8266 => [(14, 12, 13, 15), (14, 12, 13, 16)],
        TouchBoard.RaspberryPiPico => [(18, 16, 19, 17), (2, 4, 3, 5)],
        _ => [(13, 12, 11, 8), (13, 12, 11, 10), (13, 12, 11, 4), (13, 12, 11, 9)]
    };

    /// <summary>
    /// A sketch that finds the touch chip by itself: it scans the usual I2C
    /// pins for the addresses touch chips answer on, then asks the user to
    /// press the screen and watches the XPT2046 pressure reading on the usual
    /// SPI pins. The result is printed every two seconds as a KTOUCH: line
    /// that K-Launcher reads from the serial port.
    /// </summary>
    public static string GenerateFinder(TouchBoard board, bool turkish)
    {
        string T(string tr, string en) => turkish ? tr : en;
        var code = new StringBuilder();
        void Line(string text = "") => code.Append(text).Append('\n');
        var esp32 = TouchConfig.IsEsp32Family(board);

        Line("/*");
        Line($" * {T("K-Launcher dokunmatik çip bulucu", "K-Launcher touch chip finder")} — {BoardName(board)}");
        Line($" * {T("1) Bu kodu karta yükleyin.", "1) Upload this sketch to the board.")}");
        Line($" * {T("2) K-Launcher'da \"DİNLE VE AYARLA\"ya basın (ya da seri monitörü 115200'de açın).", "2) Press \"LISTEN AND SET UP\" in K-Launcher (or open the serial monitor at 115200).")}");
        Line($" * {T("3) İstendiğinde ekrana birkaç saniye basılı tutun.", "3) When asked, press and hold the screen for a few seconds.")}");
        Line($" * {T("Ek kütüphane gerekmez.", "No extra libraries needed.")}");
        Line(" */");
        Line();
        Line("#include <Wire.h>");
        Line("#include <SPI.h>");
        Line();
        Line("String result = \"KTOUCH:NONE\";");
        Line("String message;");
        Line();

        // ---- I2C scan
        Line("const char *chipForAddress(uint8_t address) {");
        Line("  switch (address) {");
        Line("    case 0x38: return \"Ft6206\";");
        Line("    case 0x5D: case 0x14: return \"Gt911\";");
        Line("    case 0x15: return \"Cst816s\";");
        Line("    default: return nullptr;");
        Line("  }");
        Line("}");
        Line();
        Line("const char *unsupportedForAddress(uint8_t address) {");
        Line("  switch (address) {");
        Line("    case 0x48: return \"NS2009 / TSC2007\";");
        Line("    case 0x41: case 0x44: return \"STMPE610\";");
        Line("    case 0x24: return \"CST820 / CST328\";");
        Line("    default: return nullptr;");
        Line("  }");
        Line("}");
        Line();
        Line("bool scanI2c(int sda, int scl) {");
        if (esp32)
            Line("  Wire.end();");
        if (board == TouchBoard.RaspberryPiPico)
        {
            Line("  Wire.end();");
            Line("  Wire.setSDA(sda);");
            Line("  Wire.setSCL(scl);");
            Line("  Wire.begin();");
        }
        else if (board is TouchBoard.Esp32 or TouchBoard.Esp32S3 or TouchBoard.Esp32C3 or TouchBoard.Esp8266)
        {
            Line("  Wire.begin(sda, scl);");
        }
        else
        {
            Line("  Wire.begin();");
        }
        Line("  delay(50);");
        Line("  for (uint8_t address = 0x08; address < 0x78; address++) {");
        Line("    Wire.beginTransmission(address);");
        Line("    if (Wire.endTransmission() != 0) continue;");
        Line("    const char *chip = chipForAddress(address);");
        Line("    if (chip) {");
        Line("      result = String(\"KTOUCH:CHIP=\") + chip + \";SDA=\" + sda + \";SCL=\" + scl;");
        Line($"      message = String(\"{T("Bulundu", "Found")}: \") + chip + \" (SDA \" + sda + \", SCL \" + scl + \")\";");
        Line("      return true;");
        Line("    }");
        Line("    const char *other = unsupportedForAddress(address);");
        Line("    if (other) {");
        Line("      result = String(\"KTOUCH:UNSUPPORTED=\") + other + \";SDA=\" + sda + \";SCL=\" + scl;");
        Line($"      message = String(\"{T("Desteklenmeyen çip", "Unsupported chip")}: \") + other;");
        Line("    }");
        Line("  }");
        Line("  return false;");
        Line("}");
        Line();

        // ---- XPT2046 pressure probe
        if (esp32)
            Line($"SPIClass probeSpi({(board == TouchBoard.Esp32 ? "VSPI" : "FSPI")});");
        Line();
        Line("template <typename Bus>");
        Line("uint16_t readPressure(Bus &spi, int cs) {");
        Line("  spi.beginTransaction(SPISettings(1000000, MSBFIRST, SPI_MODE0));");
        Line("  digitalWrite(cs, LOW);");
        Line("  spi.transfer(0xB1);");
        Line("  uint16_t z1 = spi.transfer16(0) >> 3;");
        Line("  digitalWrite(cs, HIGH);");
        Line("  spi.endTransaction();");
        Line("  return z1;");
        Line("}");
        Line();
        Line("// " + T("Ekrana basılıyken basınç yükselir, bırakınca sıfıra iner", "Pressure rises while the screen is pressed and drops to zero when released"));
        Line("bool probeXpt2046(int sck, int miso, int mosi, int cs) {");
        Line("  pinMode(cs, OUTPUT);");
        Line("  digitalWrite(cs, HIGH);");
        if (esp32)
        {
            Line("  probeSpi.begin(sck, miso, mosi, cs);");
        }
        else if (board == TouchBoard.RaspberryPiPico)
        {
            Line("  SPI.end();");
            Line("  SPI.setRX(miso);");
            Line("  SPI.setTX(mosi);");
            Line("  SPI.setSCK(sck);");
            Line("  SPI.begin();");
        }
        else
        {
            Line("  SPI.begin();");
        }
        Line("  uint16_t low = 4095, high = 0;");
        Line("  unsigned long start = millis();");
        Line("  while (millis() - start < 4000) {");
        Line(esp32 ? "    uint16_t z = readPressure(probeSpi, cs);" : "    uint16_t z = readPressure(SPI, cs);");
        Line("    if (z < low) low = z;");
        Line("    if (z > high) high = z;");
        Line("    delay(10);");
        Line("  }");
        if (esp32)
            Line("  probeSpi.end();");
        Line("  return low < 60 && high > 250 && high < 4000;");
        Line("}");
        Line();

        // ---- setup / loop
        Line("void setup() {");
        Line("  Serial.begin(115200);");
        Line("  delay(1500);");
        Line($"  Serial.println(\"{T("K-Launcher çip bulucu başladı", "K-Launcher chip finder started")}\");");
        Line();
        Line($"  // {T("1) I2C dokunmatik çipleri", "1) I2C touch chips")}");
        foreach (var (sda, scl) in FinderI2cPins(board))
        {
            var guardUsb = board == TouchBoard.Esp32S3 && sda == 19;
            if (guardUsb)
                Line("#if !ARDUINO_USB_CDC_ON_BOOT");
            Line($"  if (scanI2c({sda}, {scl})) return;");
            if (guardUsb)
                Line("#endif");
        }
        if (esp32 || board == TouchBoard.RaspberryPiPico)
            Line("  Wire.end();   // " + T("I2C pinlerini SPI denemesi için bırak", "free the I2C pins for the SPI test"));
        Line();
        Line($"  // {T("2) XPT2046: basılı tutarken basınç ölçülür", "2) XPT2046: pressure is measured while you hold the screen")}");
        Line($"  Serial.println(\"KTOUCH:PRESS\");");
        Line($"  Serial.println(\"{T("Şimdi ekrana parmağınızla basılı tutun...", "Now press and hold the screen with your finger...")}\");");
        foreach (var (sck, miso, mosi, cs) in FinderSpiPins(board))
        {
            Line($"  if (probeXpt2046({sck}, {miso}, {mosi}, {cs})) {{");
            Line($"    result = \"KTOUCH:CHIP=Xpt2046;SCK={sck};MISO={miso};MOSI={mosi};CS={cs}\";");
            Line($"    message = \"{T("Bulundu", "Found")}: XPT2046 (SCK {sck}, MISO {miso}, MOSI {mosi}, CS {cs})\";");
            Line("    return;");
            Line("  }");
        }
        Line("  if (message.length() == 0)");
        Line($"    message = \"{T("Dokunmatik çip bulunamadı. Kabloları ve pinleri kontrol edin.", "No touch chip found. Check the wiring and pins.")}\";");
        Line("}");
        Line();
        Line("void loop() {");
        Line("  Serial.println(result);");
        Line("  Serial.println(message);");
        Line("  delay(2000);");
        Line("}");
        return code.ToString();
    }

    public static string Generate(TouchConfig config, bool turkish)
    {
        string T(string tr, string en) => turkish ? tr : en;
        var code = new StringBuilder();
        void Line(string text = "") => code.Append(text).Append('\n');

        // ---------------------------------------------------------------- Header
        Line("/*");
        Line($" * {T("K-Launcher ile oluşturuldu: dokunmatik ekran kodu", "Generated by K-Launcher: touch screen code")}");
        Line($" * {T("Kart", "Board")}: {BoardName(config.Board)}");
        Line($" * {T("Dokunmatik çip", "Touch chip")}: {ChipName(config.Chip)}");
        Line($" * {T("Ekran", "Screen")}: {config.Width}x{config.Height}, {T("döndürme", "rotation")} {config.Rotation}");
        Line(" *");
        Line($" * {T("Kütüphane Yöneticisi'nden kurulacaklar", "Install from the Library Manager")}:");
        foreach (var library in RequiredLibraries(config))
            Line($" *   - {library}");
        if (config.Output is TouchOutput.TftEspiDemo or TouchOutput.LvglDriver)
        {
            Line(" *");
            Line($" * {T("TFT_eSPI için ekranınıza uygun User_Setup.h dosyasını ayarlamayı unutmayın.", "Remember to set up TFT_eSPI's User_Setup.h for your display.")}");
        }
        if (config.Output == TouchOutput.LvglDriver)
            Line($" * {T("LVGL için lv_conf.h içinde LV_USE_TFT_ESPI 1 olmalı.", "For LVGL, lv_conf.h needs LV_USE_TFT_ESPI 1.")}");
        if (config.Output != TouchOutput.SerialTest && config.Chip == TouchChip.Gt911)
        {
            Line($" * {T("4.3\"/7\" RGB paralel ekranları TFT_eSPI süremez; o kartlarda kendi ekran sürücünüzü", "TFT_eSPI cannot drive 4.3\"/7\" RGB parallel panels; on those boards keep your own display")}");
            Line($" * {T("(ör. Arduino_GFX) kullanıp buradan yalnızca touchBegin/readTouch kısmını alın.", "driver (e.g. Arduino_GFX) and take only the touchBegin/readTouch part from here.")}");
        }
        if (!TouchConfig.IsEsp32Family(config.Board) && config.Chip is TouchChip.Gt911 or TouchChip.Cst816s)
        {
            Line(" *");
            Line($" * {T("Not: Bu kütüphane en iyi ESP32 kartlarında çalışır; bu kartta I2C pinleri sabit olabilir.", "Note: this library works best on ESP32 boards; this board may have fixed I2C pins.")}");
        }
        Line(" */");
        Line();

        // -------------------------------------------------------------- Includes
        if (TouchConfig.UsesSpi(config.Chip))
        {
            Line("#include <SPI.h>");
            Line("#include <XPT2046_Touchscreen.h>");
        }
        else
        {
            Line("#include <Wire.h>");
            Line(config.Chip switch
            {
                TouchChip.Ft6206 => "#include <Adafruit_FT6206.h>",
                TouchChip.Gt911 => "#include <TAMC_GT911.h>",
                _ => "#include <CST816S.h>"
            });
        }
        if (config.Output is TouchOutput.TftEspiDemo or TouchOutput.LvglDriver)
            Line("#include <TFT_eSPI.h>");
        if (config.Output == TouchOutput.LvglDriver)
            Line("#include <lvgl.h>");
        Line();

        // --------------------------------------------------------------- Settings
        Line($"// {T("Ekran boyutu (döndürme uygulandıktan sonraki hâli)", "Screen size (after rotation)")}");
        Line($"#define SCREEN_W        {config.Width}");
        Line($"#define SCREEN_H        {config.Height}");
        Line($"#define SCREEN_ROTATION {config.Rotation}");
        Line();
        Line($"// {T("Dokunulan yer ters çıkarsa bunları 0/1 yapın", "If touches land in the wrong place, flip these between 0 and 1")}");
        Line($"#define TOUCH_SWAP_XY   {(config.SwapXY ? 1 : 0)}");
        Line($"#define TOUCH_INVERT_X  {(config.InvertX ? 1 : 0)}");
        Line($"#define TOUCH_INVERT_Y  {(config.InvertY ? 1 : 0)}");
        Line();
        Line($"// {T("Pinler (-1 = bağlı değil)", "Pins (-1 = not connected)")}");
        if (TouchConfig.UsesSpi(config.Chip))
        {
            Line($"#define TOUCH_SCK   {config.Sck}");
            Line($"#define TOUCH_MISO  {config.Miso}");
            Line($"#define TOUCH_MOSI  {config.Mosi}");
            Line($"#define TOUCH_CS    {config.Cs}");
            Line($"#define TOUCH_IRQ   {(config.Irq < 0 ? 255 : config.Irq)}   // 255 = {T("IRQ kullanılmıyor", "IRQ not used")}");
            Line();
            Line($"// {T("Kalibrasyon: seri monitördeki ham değerlere göre ayarlayın", "Calibration: adjust using the raw values on the serial monitor")}");
            Line($"#define TS_MINX {config.RawMinX}");
            Line($"#define TS_MAXX {config.RawMaxX}");
            Line($"#define TS_MINY {config.RawMinY}");
            Line($"#define TS_MAXY {config.RawMaxY}");
            Line("#define TS_MIN_PRESSURE 300");
        }
        else
        {
            Line($"#define TOUCH_SDA   {config.Sda}");
            Line($"#define TOUCH_SCL   {config.Scl}");
            if (config.Chip == TouchChip.Gt911)
            {
                Line($"#define TOUCH_INT   {config.Int}");
                Line($"#define TOUCH_RST   {config.Rst}");
            }
            else if (config.Chip == TouchChip.Cst816s)
            {
                Line($"#define TOUCH_RST   {config.Rst}");
                Line($"#define TOUCH_IRQ   {config.Irq}");
            }
        }
        Line();

        // ----------------------------------------------------------- Touch driver
        Line("int16_t touchRawX = 0, touchRawY = 0;");
        Line();
        switch (config.Chip)
        {
            case TouchChip.Xpt2046:
                WriteXpt2046(config, Line, T);
                break;
            case TouchChip.Ft6206:
                Line("Adafruit_FT6206 ctp;");
                Line();
                WriteOrientation(Line, T);
                Line("void touchBegin() {");
                Line($"  {WireBegin(config)}");
                Line("  if (!ctp.begin(40, &Wire)) {");
                Line($"    Serial.println(\"{T("FT6206 bulunamadı, kabloları kontrol edin", "FT6206 not found, check the wiring")}\");");
                Line("  }");
                Line("}");
                Line();
                Line("bool readTouch(int16_t &x, int16_t &y) {");
                Line("  if (!ctp.touched()) return false;");
                Line("  TS_Point p = ctp.getPoint();");
                Line("  touchRawX = p.x;");
                Line("  touchRawY = p.y;");
                Line("  x = p.x;");
                Line("  y = p.y;");
                Line("  applyOrientation(x, y);");
                Line("  return true;");
                Line("}");
                break;
            case TouchChip.Gt911:
                Line("TAMC_GT911 tp(TOUCH_SDA, TOUCH_SCL, TOUCH_INT, TOUCH_RST, SCREEN_W, SCREEN_H);");
                Line();
                WriteOrientation(Line, T);
                Line("void touchBegin() {");
                Line("  tp.begin();");
                Line("  tp.setRotation(ROTATION_NORMAL);");
                Line("}");
                Line();
                Line("bool readTouch(int16_t &x, int16_t &y) {");
                Line("  tp.read();");
                Line("  if (!tp.isTouched) return false;");
                Line("  touchRawX = tp.points[0].x;");
                Line("  touchRawY = tp.points[0].y;");
                Line("  x = touchRawX;");
                Line("  y = touchRawY;");
                Line("  applyOrientation(x, y);");
                Line("  return true;");
                Line("}");
                break;
            default:
                Line("CST816S touch(TOUCH_SDA, TOUCH_SCL, TOUCH_RST, TOUCH_IRQ);");
                Line();
                WriteOrientation(Line, T);
                Line("void touchBegin() {");
                Line("  touch.begin();");
                Line("}");
                Line();
                Line("bool readTouch(int16_t &x, int16_t &y) {");
                Line("  if (!touch.available() || touch.data.points == 0) return false;");
                Line("  touchRawX = touch.data.x;");
                Line("  touchRawY = touch.data.y;");
                Line("  x = touchRawX;");
                Line("  y = touchRawY;");
                Line("  applyOrientation(x, y);");
                Line("  return true;");
                Line("}");
                break;
        }
        Line();

        // ----------------------------------------------------------------- Output
        switch (config.Output)
        {
            case TouchOutput.TftEspiDemo:
                WriteTftDemo(config, Line, T);
                break;
            case TouchOutput.LvglDriver:
                WriteLvgl(config, Line, T);
                break;
            default:
                WriteSerialTest(config, Line, T);
                break;
        }

        return code.ToString();
    }

    private static void WriteXpt2046(TouchConfig config, Action<string> line, Func<string, string, string> t)
    {
        var separate = config.SeparateSpiBus && TouchConfig.IsEsp32Family(config.Board);
        if (separate)
        {
            line($"SPIClass touchSpi({(config.Board == TouchBoard.Esp32 ? "VSPI" : "FSPI")});   // {t("dokunmatik için ayrı SPI hattı", "separate SPI bus for touch")}");
        }
        line("XPT2046_Touchscreen ts(TOUCH_CS, TOUCH_IRQ);");
        line("");
        WriteOrientation(line, t);
        line("void touchBegin() {");
        if (separate)
        {
            line("  touchSpi.begin(TOUCH_SCK, TOUCH_MISO, TOUCH_MOSI, TOUCH_CS);");
            line("  ts.begin(touchSpi);");
        }
        else
        {
            switch (config.Board)
            {
                case TouchBoard.Esp32 or TouchBoard.Esp32S3 or TouchBoard.Esp32C3:
                    line("  SPI.begin(TOUCH_SCK, TOUCH_MISO, TOUCH_MOSI);");
                    break;
                case TouchBoard.RaspberryPiPico:
                    line("  SPI.setRX(TOUCH_MISO);");
                    line("  SPI.setTX(TOUCH_MOSI);");
                    line("  SPI.setSCK(TOUCH_SCK);");
                    line("  SPI.begin();");
                    break;
                default:
                    line($"  SPI.begin();   // {t("bu kartta SPI pinleri sabittir", "SPI pins are fixed on this board")}");
                    break;
            }
            line("  ts.begin();");
        }
        line("  ts.setRotation(SCREEN_ROTATION);");
        line("}");
        line("");
        line("bool readTouch(int16_t &x, int16_t &y) {");
        line("  if (!ts.touched()) return false;");
        line("  TS_Point p = ts.getPoint();");
        line("  if (p.z < TS_MIN_PRESSURE) return false;");
        line("  touchRawX = p.x;");
        line("  touchRawY = p.y;");
        line("  x = map(p.x, TS_MINX, TS_MAXX, 0, SCREEN_W - 1);");
        line("  y = map(p.y, TS_MINY, TS_MAXY, 0, SCREEN_H - 1);");
        line("  applyOrientation(x, y);");
        line("  return true;");
        line("}");
    }

    private static void WriteOrientation(Action<string> line, Func<string, string, string> t)
    {
        line($"// {t("Dokunma koordinatlarını ekran yönüne çevirir", "Turns touch coordinates to match the screen orientation")}");
        line("void applyOrientation(int16_t &x, int16_t &y) {");
        line("#if TOUCH_SWAP_XY");
        line("  int16_t swapped = x;");
        line("  x = y;");
        line("  y = swapped;");
        line("#endif");
        line("#if TOUCH_INVERT_X");
        line("  x = SCREEN_W - 1 - x;");
        line("#endif");
        line("#if TOUCH_INVERT_Y");
        line("  y = SCREEN_H - 1 - y;");
        line("#endif");
        line("  x = constrain(x, 0, SCREEN_W - 1);");
        line("  y = constrain(y, 0, SCREEN_H - 1);");
        line("}");
        line("");
    }

    private static void WriteSerialTest(TouchConfig config, Action<string> line, Func<string, string, string> t)
    {
        line("void setup() {");
        line("  Serial.begin(115200);");
        line("  delay(300);");
        line($"  Serial.println(\"{t("K-Launcher dokunmatik testi: ekrana dokunun", "K-Launcher touch test: touch the screen")}\");");
        line("  touchBegin();");
        line("}");
        line("");
        line("void loop() {");
        line("  int16_t x, y;");
        line("  if (readTouch(x, y)) {");
        line($"    Serial.print(\"{t("Dokunma", "Touch")} x=\");");
        line("    Serial.print(x);");
        line("    Serial.print(\" y=\");");
        line("    Serial.print(y);");
        line($"    Serial.print(\"   ({t("ham", "raw")} \");");
        line("    Serial.print(touchRawX);");
        line("    Serial.print(\", \");");
        line("    Serial.print(touchRawY);");
        line("    Serial.println(\")\");");
        line("  }");
        line("  delay(20);");
        line("}");
        if (config.Chip == TouchChip.Xpt2046)
        {
            line("");
            line($"// {t("Kalibrasyon: ekranın dört köşesine dokunun, en küçük ve en büyük ham değerleri", "Calibration: touch all four corners and copy the smallest and largest raw values")}");
            line($"// {t("TS_MINX / TS_MAXX / TS_MINY / TS_MAXY satırlarına yazın.", "into the TS_MINX / TS_MAXX / TS_MINY / TS_MAXY lines.")}");
        }
    }

    private static void WriteTftDemo(TouchConfig config, Action<string> line, Func<string, string, string> t)
    {
        line("TFT_eSPI tft;");
        line("");
        line("const int16_t BTN_W = 90, BTN_H = 36;");
        line("");
        line("void drawUi() {");
        line("  tft.fillScreen(TFT_BLACK);");
        line("  tft.setTextColor(TFT_WHITE, TFT_BLACK);");
        line("  tft.setTextDatum(TL_DATUM);");
        line($"  tft.drawString(\"{t("Dokun ve ciz", "Touch to draw")}\", 8, 8, 2);");
        line("  tft.fillRoundRect(SCREEN_W - BTN_W - 6, 6, BTN_W, BTN_H, 6, TFT_DARKGREY);");
        line("  tft.setTextColor(TFT_WHITE, TFT_DARKGREY);");
        line("  tft.setTextDatum(MC_DATUM);");
        line($"  tft.drawString(\"{t("TEMIZLE", "CLEAR")}\", SCREEN_W - BTN_W / 2 - 6, 6 + BTN_H / 2, 2);");
        line("}");
        line("");
        line("void setup() {");
        line("  Serial.begin(115200);");
        line("  tft.init();");
        line("  tft.setRotation(SCREEN_ROTATION);");
        line("  touchBegin();");
        line("  drawUi();");
        line("}");
        line("");
        line("void loop() {");
        line("  int16_t x, y;");
        line("  if (readTouch(x, y)) {");
        line("    bool onButton = x >= SCREEN_W - BTN_W - 6 && y <= 6 + BTN_H;");
        line("    if (onButton) {");
        line("      drawUi();");
        line("      delay(250);");
        line("    } else {");
        line("      tft.fillCircle(x, y, 3, TFT_YELLOW);");
        line("    }");
        line("  }");
        line("  delay(5);");
        line("}");
    }

    private static void WriteLvgl(TouchConfig config, Action<string> line, Func<string, string, string> t)
    {
        line("#define DRAW_BUF_SIZE (SCREEN_W * SCREEN_H / 10 * (LV_COLOR_DEPTH / 8))");
        line("uint32_t drawBuf[DRAW_BUF_SIZE / 4];");
        line("");
        line($"// {t("LVGL dokunmatik giriş sürücüsü", "LVGL touch input driver")}");
        line("void touchReadCb(lv_indev_t *indev, lv_indev_data_t *data) {");
        line("  int16_t x, y;");
        line("  if (readTouch(x, y)) {");
        line("    data->state = LV_INDEV_STATE_PRESSED;");
        line("    data->point.x = x;");
        line("    data->point.y = y;");
        line("  } else {");
        line("    data->state = LV_INDEV_STATE_RELEASED;");
        line("  }");
        line("}");
        line("");
        line("uint32_t tickMillis() {");
        line("  return millis();");
        line("}");
        line("");
        line("void buttonClicked(lv_event_t *e) {");
        line("  lv_obj_t *label = (lv_obj_t *)lv_event_get_user_data(e);");
        line($"  lv_label_set_text(label, \"{t("Dokunuldu!", "Touched!")}\");");
        line("}");
        line("");
        line("void setup() {");
        line("  Serial.begin(115200);");
        line("  touchBegin();");
        line("");
        line("  lv_init();");
        line("  lv_tick_set_cb(tickMillis);");
        // LVGL is created with the panel's own (unrotated) size and then rotated,
        // so it ends up at SCREEN_W x SCREEN_H.
        var odd = config.Rotation % 2 == 1;
        line($"  // {t("Ekranın kendi (döndürülmemiş) boyutu; LVGL sonra döndürür", "The panel's own (unrotated) size; LVGL rotates it afterwards")}");
        line($"  lv_display_t *disp = lv_tft_espi_create({(odd ? "SCREEN_H, SCREEN_W" : "SCREEN_W, SCREEN_H")}, drawBuf, sizeof(drawBuf));");
        line("  lv_display_set_rotation(disp, (lv_display_rotation_t)SCREEN_ROTATION);");
        line("");
        line("  lv_indev_t *indev = lv_indev_create();");
        line("  lv_indev_set_type(indev, LV_INDEV_TYPE_POINTER);");
        line("  lv_indev_set_read_cb(indev, touchReadCb);");
        line("");
        line($"  // {t("Örnek: dokununca yazısı değişen bir düğme", "Example: a button whose text changes when touched")}");
        line("  lv_obj_t *btn = lv_button_create(lv_screen_active());");
        line("  lv_obj_set_size(btn, 160, 60);");
        line("  lv_obj_center(btn);");
        line("  lv_obj_t *label = lv_label_create(btn);");
        line($"  lv_label_set_text(label, \"{t("Dokun", "Touch me")}\");");
        line("  lv_obj_center(label);");
        line("  lv_obj_add_event_cb(btn, buttonClicked, LV_EVENT_CLICKED, label);");
        line("}");
        line("");
        line("void loop() {");
        line("  lv_timer_handler();");
        line("  delay(5);");
        line("}");
    }

    private static string WireBegin(TouchConfig config) => config.Board switch
    {
        TouchBoard.Esp32 or TouchBoard.Esp32S3 or TouchBoard.Esp32C3 or TouchBoard.Esp8266 =>
            "Wire.begin(TOUCH_SDA, TOUCH_SCL);",
        TouchBoard.RaspberryPiPico =>
            "Wire.setSDA(TOUCH_SDA); Wire.setSCL(TOUCH_SCL); Wire.begin();",
        _ => "Wire.begin();"
    };

    public static string BoardName(TouchBoard board) => board switch
    {
        TouchBoard.Esp32S3 => "ESP32-S3",
        TouchBoard.Esp32C3 => "ESP32-C3",
        TouchBoard.Esp8266 => "ESP8266",
        TouchBoard.RaspberryPiPico => "Raspberry Pi Pico",
        TouchBoard.ArduinoAvr => "Arduino Uno / Mega",
        _ => "ESP32"
    };

    public static string ChipName(TouchChip chip) => chip switch
    {
        TouchChip.Ft6206 => "FT6206 / FT6236",
        TouchChip.Gt911 => "GT911",
        TouchChip.Cst816s => "CST816S",
        _ => "XPT2046"
    };
}
