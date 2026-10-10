# بررسی تست‌های مزستا در برابر OCCT، AIDA64، Prime95، MemTest86، Geekbench، BurnInTest و CrystalDisk

تاریخ: ۱۴۰۵/۰۷/۱۸ (2026-10-10). شاخه: `slice-14/ai-assistant` (کامیت `df5400c`).
وضعیت: پژوهش و برنامه. هنوز هیچ کدی تغییر نکرده است.

یادداشت صادقانه درباره‌ی منابع: سایت‌های ocbase.com و testmem5.com از محیط ابری باز نشدند. پس جزئیات OCCT 17 از
خبرهای VideoCardz و KitGuru و راهنمای occt.app است، به‌علاوه‌ی عکس‌هایی که فرستادید. هر جا چیزی تأیید نشد، نوشته شده
«تأیید نشده».

---

## ۱. الان چه داریم (خلاصه از کد)

| بخش | تست‌ها | روش بررسی صحت |
|---|---|---|
| CPU | `cpu.stress` (ماتریس + عدد صحیح + هش/Deflate به نوبت، ثابت یا نوسانی)، `cpu.vector` (FMA روی AVX-512/AVX2/SSE2)، `cpu.linpack` (LU با residual از نوع HPL و تطبیق بیت‌به‌بیت مثل IntelBurnTest)، `cpu.fft` (۴۰۹۶ و یک میلیون نقطه، مثل Prime95)، `cpu.singlecore` (چرخش هسته به هسته مثل CoreCycler، با مکث تصادفی)، `cpu.integer`، `cpu.hash`، `cpu.matrix` | هر بلاک با جوابی که از قبل حساب شده مقایسه می‌شود، نه با جواب خود دستگاه. **این از OCCT هم سخت‌گیرانه‌تر است.** |
| RAM | `memory.pattern` (الگوهای MemTest86: walking 1/0، 0xA5/0x5A، آدرس و معکوس آدرس، تصادفی + moving inversions / block move / stride در هر پاس چهارم)، `memory.bitfade` (قفل در RAM با VirtualLock) | هر پاس کل حافظه بررسی می‌شود |
| GPU | `gpu.steady/variable/pulse`، `gpu.vram` (۸ الگو، بررسی روی خود GPU)، `gpu.render`، `gpu.scene.d3d`، `gpu.ai` (DirectML) | نمونه‌ای از نخ‌ها روی CPU دوباره حساب می‌شود، فریم‌ها مقایسه می‌شوند |
| برق | `power.combined` (CPU + GPU با هم) | هر طرف بررسی خودش را دارد |
| دیسک | `storage.sequential`، `storage.random4k` (با verify)، `storage.smart` (SMART + لاگ سلامت NVMe)؛ بنچمارک SEQ1M Q8، RND4K Q32/Q1، ترکیب ۷۰/۳۰ | |
| همه‌ی تست‌ها | WHEA در طول تست = Failed؛ خطاهای PCIe کارت زیر بار؛ checkpoint برای ریست و کرش؛ دمای DIMM از SPD | |

**نتیجه:** پایه‌ی کار درست و استاندارد است. «اعداد الکی» حساب نمی‌کنیم: هر تست جواب را با مرجع مستقل بررسی می‌کند. این همان
اصلی است که Prime95، Linpack و y-cruncher رویش بنا شده‌اند. کمبودها در **پوشش** است، نه در درستی:
- حالت «کار سبک و روزمره» نداریم.
- RAM ما مدل بلوکی TM5/OCCT را ندارد.
- تست از نوع y-cruncher (عدد صحیح بزرگ و حافظه) نداریم.
- تست 7-Zip/LZMA نداریم.
- تست پرکردن کش SLC دیسک نداریم.
- بررسی ROP کارت‌های سری ۵۰ را نداریم.

---

## ۲. ابزارهای مرجع از چه تکنیک‌هایی استفاده می‌کنند

### OCCT 17: تست ماژولار RAM (همان عکس‌های شما)
- ۱۶ «بلوک» دارد که از TM5 و MemTest86 الهام گرفته‌اند. OCCT می‌گوید آن‌ها را از نو نوشته است.
- دسته‌بندی بلوک‌ها: non-idempotent، idempotent، check، all-in-one، utility.
- **خطا فقط وقتی دیده می‌شود که یک بلوک Check در زنجیره باشد**، مگر در presetهای all-in-one.
- presetها: Spike، Bit Fade، Heat، TM5 Extreme و چند تای دیگر. در نسخه‌ی پایدار شش preset هست.
- فایل تنظیمات TM5 را هم می‌شود وارد کرد.

**عکس آخر شما یعنی چه:** Copy to Custom، preset را به زنجیره‌ی بلوک‌هایش باز می‌کند:

```
Loop Start ×32
   TM5 Refresh  (AVX2)       ← کل حافظه را با یک الگوی شناخته‌شده پر می‌کند
   Mirror Move 128 (AVX2) ↷0x256  ← داده را N بار بین دو نیمه‌ی آینه‌ای جابه‌جا می‌کند (128 = پهنای بیت)
   Check                      ← یک بار، در آخر: حالت نهایی باید دقیقاً همان باشد که از قبل حساب شده
Loop End
Loop Start ×32  … همان، با 0x512 تکرار
Loop Start ×32  TM5 Arithmetic ↷0x16 seed 0x0 …   ← عملیات حسابی روی خود داده‌های حافظه
```

**نکته‌ی کلیدی الگوریتمی:** بلوک‌های «non-idempotent» داده را تغییر می‌دهند، ولی طوری که حالت نهایی‌شان قابل
پیش‌بینی است. پس می‌شود صدها بار با سرعت کامل باس خواند و نوشت و **فقط یک بار** در آخر بررسی کرد. نتیجه: ترافیک و گرمای
حافظه چند برابر روش ما (که هر پاس را بررسی می‌کند) است. یک خطای گذرا هم در کل زنجیره پخش می‌شود و گم نمی‌شود.

presetها (از عکس‌های شما):
- **Spike:** پایداری در برابر پرش و افت شدید بار (کنترلر حافظه، VSOC و VDDIO).
- **Bit Fade:** هر ۱۹ دقیقه دنبال بیت‌های تغییرکرده می‌گردد. ما هم داریم.
- **Heat:** مصالحه‌ی خوب بین مصرف برق و پیدا کردن خطا، بهترین برای DDR5. یعنی ماژول را داغ می‌کند.
- **TM5 Extreme:** برگرفته از پیکربندی Extreme1 کاربر anta777.

### TestMem5 (TM5) و پیکربندی anta777 Extreme1
- ۱۶ تست، Time 500%، سه سیکل. نام توابع: SimpleTest، RefreshStable، MirrorMove، MoveInvVar و چند تای دیگر.
- MirrorMove بلاک را آینه‌وار جابه‌جا می‌کند و خودش بررسی نمی‌کند. بررسی جداست، دقیقاً همان مدلی که OCCT برداشته.
- معنی دقیق تک‌تک توابع را از منبع رسمی تأیید نکردم.

### MemTest86
- تست‌های ۰ تا ۱۳: address، walking، moving inversions (۸ بیتی، تصادفی، ۶۴ بیتی)، block move، modulo-20، bit fade، random number sequence.
- **Test 13 همان Row Hammer است:** دو آدرس در یک bank را پشت سر هم می‌خواند و ردیف‌های همسایه را بررسی می‌کند.
  اگر فقط در پاس سریع خطا پیدا شود، هشدار می‌دهد. G.Skill این تست را معیار پایداری نمی‌داند و این موضوع مورد اختلاف است.
- MemTest86 در ویندوز اجرا نمی‌شود و آدرس فیزیکی و اسلات را می‌شناسد. ما در ویندوز آدرس فیزیکی نداریم، که در کد هم نوشته شده.

### y-cruncher (محبوب‌ترین تست برای DDR5 و Ryzen)
- حالت stress زیرتست‌های BKT، BBP، SFTv4، SNT، SVT، FFTv4، N63 و VT3 را به نوبت اجرا می‌کند.
- **N63** ضرب عدد صحیح بزرگ است و **VT3** پیمایش برداری است. خطا در این دو معمولاً از VSOC و VDDIO و تایمینگ
  حافظه می‌آید، نه از هسته.
- اورکلاکرها روی DDR5 معمولاً دو ساعت y-cruncher با TM5 اجرا می‌کنند.
- **چیزی که ما کم داریم:** ضرب عدد صحیح خیلی بزرگ (NTT) روی حافظه‌ی زیاد. FFT ما اعشاری است.

### Prime95
- Small FFT در کش جا می‌شود و بیشترین گرما و AVX را می‌دهد. Large FFT به RAM و کنترلر حافظه فشار می‌آورد. Blend هر دو است.
- هر تکرار با residue (باقی‌مانده) بررسی می‌شود.
- ما معادلش را داریم (`cpu.fft`) و فقط حالت «اندازه‌ی ثابت کوچک برای بیشترین گرما» را نداریم.

### AIDA64
- System Stability Test با زیرتست‌های CPU، FPU (AVX)، Cache، Memory، Disk و GPU، که با هم یا جدا اجرا می‌شوند.
- Cache & Memory Benchmark: پهنای باند و تأخیر L1، L2، L3 و RAM.
- ما تأخیر را در چند اندازه داریم (`LatencySizes`) و پهنای باند L1 تا L3 را نداریم.

### Geekbench 6
- بارهای کاری واقعی: File Compression، Navigation، HTML5 Browser، PDF Render، Photo Library، Clang، Text Processing،
  Asset Compression، Object Detection، Background Blur، Object Remover، Horizon Detection، Photo Filter، HDR،
  Ray Tracer، Structure from Motion.
- مدل چند هسته‌ای «shared task» دارد: نخ‌ها روی یک کار مشترک کار می‌کنند، که به نرم‌افزار واقعی شبیه‌تر است.
- **درس برای ما:** بنچمارک از بارهای واقعی ساخته شود، نه فقط ضرب ماتریس.

### 7-Zip (`7z b`)
- فشرده‌سازی و بازکردن LZMA. امتیاز به MIPS است که نسبت به Core 2 تک‌نخی نرمال شده.
- فشرده‌سازی به تأخیر RAM و کش و TLB حساس است. بازکردن به ضرب عدد صحیح ۳۲×۱۶، پرش‌های پیش‌بینی‌نشده و شیفت وابسته است.
- خودش CRC را بررسی می‌کند. مستنداتش می‌گوید خطا در آن **معمولاً یعنی RAM خراب**.
- عددش را همه می‌شناسند. یک فهرست عمومی بزرگ از امتیازها وجود دارد.

### PassMark BurnInTest
- CPU، RAM، دیسک، 2D، 3D، GPGPU، VRAM، پخش ویدیو، شبکه، صدا، USB و سریال (با loopback) و PCIe (با سخت‌افزار جانبی).
- **Duty cycle** برای هر قطعه جداگانه: همه‌ی قطعه‌ها هم‌زمان و هر کدام با درصد بار خودش. این به «کار سبک واقعی» نزدیک است.

### CrystalDiskMark / CrystalDiskInfo
- CDM: SEQ1M Q8T1، SEQ1M Q1T1، RND4K Q32T16 و RND4K Q1T1، روی فایل ۱ GiB، پنج بار، با داده‌ی تصادفی یا صفر.
- CDM رفتار بعد از پر شدن کش SLC و داغ شدن را **نشان نمی‌دهد**. برای آن تست نوشتن طولانی لازم است.
- CDI: ویژگی‌های SMART (Reallocated، Pending، Uncorrectable، **CRC Error** که یعنی کابل، Wear) و لاگ NVMe
  (Percentage Used، Available Spare، Media Errors).

### سخت‌افزار جدید: چیزهایی که تست باید بداند
- **DDR5 (تا ۹۰۰۰ با CUDIMM):** ECC روی خود تراشه (on-die ECC) خطای تک‌بیتی را **بی‌صدا** درست می‌کند و CPU آن را
  نمی‌بیند. پس هر خطایی که تست ما ببیند یعنی ECC داخلی هم شکست خورده، و این جدی است. حاشیه‌ی واقعی را فقط با داغ کردن
  و فشار طولانی می‌شود دید. درصد قابل توجهی از DDR5ها در برابر Row Hammer آسیب‌پذیرند.
- **RTX 50:** حدود ۰٫۵٪ کارت‌ها با **ROP کمتر** عرضه شدند. تعداد درست طبق اعلام NVIDIA: 5090 = 176، 5080 = 112،
  5070 Ti = 96. GPU-Z این عدد را از NVAPI می‌خواند. ما باید هم بررسی کنیم. اتصال 12V-2x6 هم قابل توجه است.
- **RX 9070 XT و کارت‌های جدید:** دمای hotspot و دمای حافظه. اختلاف زیاد hotspot با دمای لبه یعنی خمیر یا نصب بد.
- **Intel Core Ultra و Ryzen 9 X3D:** چند CCD، هسته‌های P و E، و پیوند die-to-die. خطا معمولاً روی **یک هسته‌ی خاص**
  یا **بین CCDها** است.

---

## ۳. مشکل اصلی شما: «زیر بار سنگین مشکل پیدا نمی‌شود، در کار روزمره پیدا می‌شود»

این مشکل واقعی و شناخته‌شده است:
- **Intel نسل ۱۳ و ۱۴ (Vmin shift):** ریشه در درخواست ولتاژ بالا در **حالت بیکار و کار سبک** بود.
- **Ryzen با Curve Optimizer منفی:** Prime95 را رد می‌کند (یعنی با موفقیت می‌گذراند) و موقع بیکاری یا تماشای ویدیو کرش می‌کند.
  طبق گزارش کاربران، حتی CoreCycler هم همیشه پیدایش نمی‌کند.

**علت فنی:** زیر بار کامل، ولتاژ و فرکانس ثابت‌اند. خطا در **گذارها** رخ می‌دهد:
- بیرون آمدن از C-state عمیق
- پرش ناگهانی یک هسته‌ی بیکار به بالاترین boost
- افت بار، که ولتاژ را overshoot یا undershoot می‌کند
- عوض شدن FCLK و حالت‌های کم‌مصرف کنترلر حافظه
- بیکاری طولانی

**پیشنهاد: تست جدید `system.daily` (کار روزمره / بار سبک)**، چند ساعته یا شبانه:
1. **ضربه‌های کوتاه روی هسته‌های تصادفی:** هر ضربه ۱ تا ۵۰ میلی‌ثانیه کار بررسی‌شده است (همان بلاک‌های ماتریس و
   عدد صحیح با جواب از پیش حساب‌شده). بعدش ۱۰ میلی‌ثانیه تا ۲ ثانیه خواب تصادفی، تا هسته به C-state برود و دوباره از
   صفر boost بگیرد. هر هسته‌ی فیزیکی باید پوشش داده شود (مثل `cpu.singlecore`).
2. **بار سبک چندنخی:** ۲ تا ۴ نخ با ۱۰ تا ۳۰ درصد بار، مثل مرورگر و برنامه‌ی اداری.
3. **کارهای واقعی کوچک:** باز کردن فشرده (LZMA یا Brotli)، SHA، پارس JSON و متن، decode عکس با WIC، و I/O
   کوچک دیسک با verify. همه با جواب از پیش معلوم.
4. **GPU سبک:** رندر ۶۰ فریم با بار کم، مثل ویدیو یا دسکتاپ. اگر بشود، decode ویدیوی سخت‌افزاری با Media Foundation،
   همان چیزی که BurnInTest «پخش ویدیو» می‌نامد.
5. **دوره‌های بیکاری کامل ۳۰ تا ۱۲۰ ثانیه‌ای:** فقط یک تایمر هر چند ثانیه یک کار کوچک بررسی‌شده انجام می‌دهد. کرش موقع
   بیکاری را checkpoint موجود (`TestSessionCheckpoint`) بعد از ری‌استارت پیدا می‌کند و نام می‌برد.
6. **داوری:** خطای محاسبه، WHEA، خطای PCIe و ری‌استارت. هیچ‌کدام نباشد = Passed. هر کدام باشد، با زمان و هسته‌اش ثبت می‌شود.

این تست اعداد ریاضی سنگین حساب نمی‌کند. **گذارها** را بارها و بارها تکرار می‌کند، که در کار روزمره رخ می‌دهند.
هیچ برنامه‌ی تجاری‌ای این را به شکل یک تست واحد ندارد. BurnInTest با duty cycle و CoreCycler نزدیک‌ترین‌ها هستند.

---

## ۴. برنامه‌ی پیشنهادی، به ترتیب ارزش

| # | چه چیزی | شبیه کدام | لایه / فایل | اندازه | چرا |
|---|---|---|---|---|---|
| 1 | **`system.daily`** کار سبک و گذارها (بخش ۳) | BurnInTest duty cycle + CoreCycler | `Diagnostics/Cpu/` تست جدید + profile جدید | متوسط | دقیقاً مشکلی که گفتید |
| 2 | **موتور بلوکی RAM**: بلوک‌های Refresh (پرکردن)، Mirror Move، Moving Inversions با الگوی متغیر، Arithmetic، Random-address (modulo-20)، Check + حلقه | OCCT 17، TM5 | `Diagnostics/Memory/` (الگوهای فعلی بلوک می‌شوند) | بزرگ | ترافیک و گرمای چند برابر، و خطاهای DDR5 که تست فعلی نمی‌بیند |
| 3 | **presetهای RAM**: Quick، Heat (بیشترین پهنای باند با AVX2 + پایش دمای SPD تا دمای هدف)، Spike (بار کامل و بیکاری به نوبت برای IMC و VSOC)، Extreme (زنجیره‌ی طولانی)، Bit Fade (موجود) | OCCT 17 | همان + `TestProfiles` | کوچک (بعد از ۲) | |
| 4 | **عدد صحیح بزرگ و حافظه** (NTT ضرب اعداد میلیون‌رقمی، جواب با باقی‌مانده‌ی پیمانه‌ای بررسی شود) | y-cruncher N63/VT3 | `Diagnostics/Cpu/CpuNttExecutor` | متوسط | خطاهای VSOC، FCLK و IMC که FFT اعشاری نمی‌بیند |
| 5 | **7-Zip**: هم تست (فشرده‌سازی و بازکردن با بررسی CRC) و هم بنچمارک MIPS | 7-Zip | دو راه، پایین را ببینید | کوچک | هم پایداری RAM و CPU، هم عددی که مشتری می‌شناسد |
| 6 | **ROPهای RTX 50**: تعداد را از NVAPI بخوان و با جدول NVIDIA مقایسه کن | GPU-Z | `Hardware/Nvidia` + `GpuCheck` | کوچک | ۰٫۵٪ کارت‌ها معیوب‌اند و مشتری خبر ندارد |
| 7 | **تست کش SLC و نوشتن طولانی**: نوشتن ۵۰ تا ۸۰٪ فضای خالی، نمودار سرعت در زمان، دمای NVMe | AJA، HD Tune | `Storage/` | متوسط | اندازه‌ی کش، سرعت بعد از کش، throttle |
| 8 | **پروفایل استاندارد CrystalDiskMark** (SEQ1M Q1T1 و RND4K Q32T16 اضافه شود، و داده‌ی تصادفی در برابر صفر) | CDM | `StorageBenchmark` | کوچک | عدد قابل مقایسه با CDM |
| 9 | **اسکن سطح دیسک (فقط خواندن)**: تأخیر هر بلاک و پیدا کردن ناحیه‌های کند | HD Tune، Victoria | `Storage/` | متوسط | سکتور ضعیف قبل از خراب شدن |
| 10 | **پهنای باند کش L1/L2/L3 و تأخیر بین هسته‌ها** (core-to-core، بین CCDها) | AIDA64 | `MemoryBenchmark` + بنچمارک جدید | کوچک تا متوسط | X3D، چند CCD، die-to-die در Intel |
| 11 | **Small FFT با بیشترین گرما** (اندازه‌ی ثابت در L2، مسیر AVX-512) | Prime95 | `CpuFftExecutor` گزینه‌ی جدید | کوچک | |
| 12 | **پایداری فریم‌ریت** در scene: درصد ثبات بین تکرارها، با معیار ۹۷٪ | 3DMark Stress Test | `GpuSceneBenchmark` | کوچک | throttle حرارتی و برقی کارت |
| 13 | **Row Hammer (اختیاری، با هشدار)** | MemTest86 Test 13 | `Memory/` | متوسط | بدون آدرس فیزیکی در ویندوز ضعیف است. **پیشنهاد نمی‌کنم** مگر بعداً |

**درباره‌ی 7-Zip دو راه هست:**
- **(الف)** همراه کردن `7zr.exe` (LGPL، کمتر از ۱ مگابایت) و اجرای `7zr b`. عدد **واقعی و قابل مقایسه** با فهرست عمومی
  7-Zip است. باید در `THIRD-PARTY-NOTICES` ثبت شود.
- **(ب)** کد C# بسته‌ی LZMA SDK (public domain) داخل خود برنامه. عددش با 7-Zip برابر نیست و باید «LZMA مزستا»
  نامیده شود.

پیشنهاد من (الف) برای بنچمارک است، و برای تست هم همان با `-mmt` و چند تکرار، چون CRC را خودش بررسی می‌کند.
اگر نمی‌خواهید فایل اجرایی خارجی همراه برنامه باشد، (ب).

**چیزهایی که پیشنهاد نمی‌کنم:**
- تست USB و سریال با loopback، و PCIe با سخت‌افزار جانبی: نیاز به سخت‌افزار دارد.
- وارد کردن فایل تنظیمات TM5: کسی در تعمیرگاه به آن نیاز ندارد.
- کپی کردن نام برنامه‌های تجاری روی تست‌ها: طبق قاعده‌ی صداقت برنامه، «شبیه» گفته شود، نه «همان».

---

## ۴٫۵. چه چیزی ساخته شد (شاخهٔ slice-14، ۱۴۰۵/۰۷/۱۸)

| تست | شناسه | چه می‌کند |
|---|---|---|
| رم · تست ترتیبی | `memory.sequence` | مرحله‌های OCCT 17 / TM5: fill (simple، random، walk1، walk0، checker، zeros، ones)، arith، invert، flip، mirror N، read، idle، check و `loop N { … }`. پیش‌تنظیم‌ها: گرما (پیش‌فرض)، سخت (شبیه Extreme1)، ضربه‌ای، حاشیه (شبیه AM5 Margin Probe)، سریع، و ترتیب دلخواه. هر check همهٔ کلمه‌ها را با چیزی مقایسه می‌کند که مرحله‌ها باید باقی گذاشته باشند. این مقدار از قبل حساب شده است: arith و invert نگاشت‌های آفین به پیمانهٔ ۲^۶۴ هستند و mirror یک جایگشت است. |
| پردازنده · چرخهٔ پیش‌تنظیم | `cpu.cycle` | تست‌های CPU به‌صورت مرحله: SSE، AVX2، AVX-512، FFT، لینپک، بار نوسانی و بوست تک‌هسته. پیش‌تنظیم‌ها: کامل، دستورالعمل‌ها، گذرا، دلخواه. مرحله‌ای که پردازنده اجرا نمی‌کند رد می‌شود و این در نتیجه نوشته می‌شود. |
| کارت گرافیک · چرخهٔ پیش‌تنظیم | `gpu.cycle` | بار ثابت، نوسانی و ضربه‌ای، VRAM و صحنهٔ سه‌بعدی. پیش‌تنظیم‌ها: کامل، گذرا (سه ریتم)، حافظه. کارت فقط یک بار انتخاب می‌شود. |
| سیستم · کار روزمره | `system.daily` | ضربه‌های کوتاه روی هر هسته به نوبت، بار سبک، ضربهٔ همهٔ هسته‌ها، کارهای روزمره (فشرده‌سازی، مرتب‌سازی، JSON) و بیکاری با بیدار شدن. طول هر دور قابل تنظیم است (پیش‌فرض ۶۰ دقیقه) و نتیجه می‌گوید چند دور کامل انجام شد. |
| دیسک · کار روزمره | `storage.daily` | درخواست‌های ترکیبی ۴ کیلوبایت تا ۱ مگابایت با مکث (تا دیسک به حالت کم‌مصرف برود)، کپی ۱۲۸ مگابایت با خود ویندوز و خواندن دوبارهٔ آن بدون کش، ۲۰۰ فایل کوچک، و بیدار شدن از بیکاری. همهٔ بایت‌ها بررسی می‌شوند و توقف‌های بیش از یک ثانیه گزارش می‌شوند. |
| پروفایل «کار روزمره» | `everyday` | system.daily ۶۰ دقیقه + memory.sequence ۱۵ دقیقه + storage.daily ۳۰ دقیقه + SMART |
| بنچمارک 7-Zip | `bench.cpu.7zip` | بنچمارک خود 7-Zip (`7z b`، با همهٔ رشته‌ها یا یک رشته) با همان 7-Zip نصب‌شده روی سیستم اجرا می‌شود. امتیاز MIPS آن با عددی که سایت‌ها و فروشگاه‌ها می‌دهند قابل مقایسه است. خطای CRC یعنی رد شدن تست. اگر 7-Zip نصب نباشد، نتیجه «پشتیبانی نمی‌شود» است. |
| دیسک · نوشتن طولانی | `storage.sustained` | یک نوشتن پیوسته (پیش‌فرض ۳۲ گیگابایت) که هر ۲۵۶ مگابایتش جدا زمان‌گیری می‌شود. نتیجه نشان می‌دهد کش SLC کجا تمام می‌شود و سرعت بعد از آن چقدر است. در آخر همهٔ بایت‌ها خوانده و بررسی می‌شوند. |
| اورکلاک/آندرولت · جهش بار | `GpuLoadKind.Transient` | صحنهٔ باغ به‌صورت جهشی از حالت بیکار اجرا می‌شود، و در هر جهش همهٔ هسته‌های CPU هم زیر بار می‌روند؛ همان پرشی که یک کلیک در ادیتور Unreal ایجاد می‌کند. این آخرین تأیید هر جست‌وجوی خودکار است و در «تست صحنه» صفحهٔ اورکلاک هم به‌صورت گزینه وجود دارد. |

ساخته نشد:
- **بررسی ROP کارت‌های RTX 50:** به یک بنچمارک fill-rate با شیدر تازه نیاز دارد، که در این محیط نه ساخته می‌شود و نه آزمایش.
- **پروفایل جدا برای CrystalDiskMark:** لازم نشد، چون `bench.storage` همین الان SEQ1M Q8، RND4K Q32 و RND4K Q1 را می‌سنجد.

صفحهٔ تست‌ها:
- پیش‌تنظیم هر تست در حالت ساده هم دیده می‌شود.
- مرحله‌های دلخواه در یک کادر پهن نوشته می‌شوند.
- پروفایل‌ها دکمه‌هایی با مدت زمانشان هستند.
- نوار پایین در پنجرهٔ باریک به خط دوم می‌رود.
- ردیف‌ها در پنجرهٔ کوتاه فشرده‌ترند.
- زیر ۱۲۰۰ پیکسل، تا وقتی کاربر خودش انتخاب نکرده، منوی کناری باریک شروع می‌شود.

**هنوز روی سخت‌افزار واقعی اجرا نشده است.** در محیط ابری لینوکس فقط بخش‌های Core و Diagnostics ساخته و آزمایش شدند (۳۱ تست واحد تازه پاس شدند).
پروژهٔ GPU و خود برنامه فقط روی ویندوز ساخته می‌شوند.

## ۵. سؤال‌هایی که قبل از ساخت باید جواب بدهید

1. از کجا شروع کنیم؟ پیشنهاد من **۱ (کار روزمره)**، بعد **۲ و ۳ (RAM بلوکی)**، بعد **۵ و ۶** است.
2. 7-Zip با فایل اجرایی واقعی (الف) یا داخل برنامه (ب)؟
3. تست کار روزمره در کدام پروفایل‌ها باشد؟ پیشنهاد: پروفایل `transient` و یک پروفایل جدید «شبانه».

## منابع
- [OCCT v17 beta – VideoCardz](https://videocardz.com/newz/occt-v17-beta-launches-with-rebuilt-memory-test-inspired-by-testmem5-and-memtest86)
- [OCCT v17 stable – VideoCardz](https://videocardz.com/newz/occt-v17-stable-released-with-modular-memory-test-and-new-launcher)
- [OCCT v17 – KitGuru](https://www.kitguru.net/gaming/joao-silva/occt-v17-arrives-with-modular-memory-test-and-performance-upgrades/)
- [OCCT guides](https://occt.app/guides.html)
- [TM5 custom configs – overclock.net](https://www.overclock.net/threads/memory-testing-with-testmem5-tm5-with-custom-configs.1751608/)
- [MemTest86 test descriptions](https://memtest86.com/tech_individual-test-descr.html)
- [MemTest86 ECC](https://www.memtest86.com/ecc.htm)
- [DDR5 Rowhammer – USENIX Sec 2025](https://usenix.org/conference/usenixsecurity25/presentation/jattke)
- [Google: Rowhammer research](https://blog.google/security/supporting-rowhammer-research-to/)
- [ECC and DDR5](https://etbe.coker.com.au/2026/07/19/ecc-ddr5/)
- [On-die ECC – arXiv 2301.07271](https://arxiv.org/pdf/2301.07271)
- [y-cruncher stress – Level1Techs](https://forum.level1techs.com/t/nucbox-k8-plus-8-core-96gb-ryzen-7-8845hs-random-crashes-under-load-y-cruncher/254481)
- [y-cruncher N63 – HWBOT](https://community.hwbot.org/topic/241639-some-questions-about-y-crunchers-n63-ex-n32n64/)
- [7-Zip benchmark](https://7-zip.opensource.jp/chm/cmdline/commands/bench.htm)
- [Geekbench 6 internals](https://geekbench.com:443/doc/geekbench6-benchmark-internals.pdf)
- [Evaluating Geekbench 6 – Chips and Cheese](https://chipsandcheese.com/p/evaluating-geekbench-6)
- [BurnInTest](https://www.passmark.com/products/bit.htm)
- [BurnInTest editions](https://www.passmark.com/products/burnintest/editions.php)
- [CrystalDiskMark metrics](https://www.digitalcitizen.life/crystaldiskmark-metrics-meaning/)
- [CrystalDiskMark and sustained writes – Igor'sLAB](https://www.igorslab.de/kioxia-exceria-pro-g2-4tb-im-test-schnell-kuehl-und-leider-auch-teuer/4/)
- [RTX 50 missing ROPs – Puget](https://www.pugetsystems.com/blog/2025/02/26/nvidia-geforce-rtx-50-series-missing-rops-issue/)
- [Intel 13th/14th Gen instability root cause](https://community.intel.com/t5/Blogs/Tech-Innovation/Client/Intel-Core-13th-and-14th-Gen-Desktop-Instability-Root-Cause/post/1750755)
- [CoreCycler – overclock.net](https://www.overclock.net/threads/corecycler-tool-for-testing-single-core-stability-e-g-curve-optimizer-settings.1777398/)
- [Ryzen CO idle crashes – AMD community](https://community.amd.com:443/t5/pc-processors/unable-to-stabilize-5900x-running-with-pbo-and-curve-optimizer/td-p/481119)
