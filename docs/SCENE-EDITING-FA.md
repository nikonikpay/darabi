# فایل‌های صحنهٔ سه‌بعدی و روش تغییر ظاهر آن، بدون سنگین‌شدن

## فایل‌ها کجاست

| چه | کجا | در گیت؟ |
|---|---|---|
| کد برنامه و آزمون‌ها | `D:\DFM APp\Cloudy\Mazesta Test App Cloudy\Mazesta` | بله (شاخه‌ها در GitHub) |
| **منبع صحنه برای Blender** | `D:\DFM APp\Cloudy\Mazesta Test App Cloudy\Mazesta-Art\courtyard-v12.blend` (به‌همراه `assets\` و `assets\v13-additions.blend`) | **نه** (۳۲۰ مگابایت؛ خودتان نسخهٔ پشتیبان نگه دارید) |
| صحنهٔ صادرشده که برنامه می‌کشد | `src\Mazesta.Diagnostics.Gpu\Scene\garden.mzscene` (۳۰ مگابایت) | بله |
| نور بازتاب‌شده‌ی از‌پیش‌محاسبه‌شده | `src\Mazesta.Diagnostics.Gpu\Scene\garden.light` (۳٫۵ مگابایت) | بله |
| شیدرها (HLSL) و نسخهٔ کامپایل‌شده‌شان | `src\Mazesta.Diagnostics.Gpu\Shaders\*.hlsl` و `*.cso` | بله |
| کد باد، باران، گیاهان، رندر | `src\Mazesta.Diagnostics.Gpu\Scene\` (`GardenWind.cs`، `GardenWeather.cs`، `GardenRaster.cs`، …) | بله |
| آزمایشگاه پیش‌نمایش (V13 و V14، `Preview-v2` تا `v4`) | `D:\DFM APp\Cloudy\Garden-Quality-Lab` | جدا از برنامه؛ هر چه آنجا ساخته‌اید خودکار وارد برنامه نمی‌شود |

برنامه خودِ `.blend` را نمی‌خواند. خروجی `garden.mzscene` در فایل اجرایی گذاشته می‌شود؛ پس تغییر ظاهر یعنی: ویرایش در Blender، صادرکردن، ساخت دوبارهٔ برنامه.

## با چه برنامه‌هایی

* **مدل، ماده، گیاه، چیدمان:** Blender (روی این سیستم Blender 4.5 و 5.2 نصب است: `C:\Program Files\Blender Foundation`؛ من نمی‌دانم اسکریپت‌ها با کدام نسخه آزموده شده‌اند؛ اول با 4.5 امتحان کنید).
* **شیدر (نور، آب، برگ، باد روی گیاه):** هر ویرایشگر متن، ترجیحاً VS Code با افزونهٔ HLSL، یا Visual Studio. بعد از تغییر: `pwsh tools/compile-gpu-shaders.ps1` (از `dxc` که با Windows SDK آمده استفاده می‌کند).
* **کد C#:** Visual Studio 2022، Rider یا VS Code.
* **بافت جدید:** هر برنامهٔ تصویری (Krita، Photoshop)؛ بافت‌ها JPEG و ۱۰۲۴×۱۰۲۴ برای ساختمان و ۵۱۲×۵۱۲ برای بقیه‌اند.

## ویرایش دیداری و اجرای export_garden.py

برنامهٔ دیگری (Unreal، Unity، ...) جای Blender را نمی‌گیرد: اسکریپت صادرکننده فقط داخل Blender (با `bpy`) کار می‌کند و صحنه را به قالب اختصاصی `garden.mzscene` می‌نویسد؛ Unreal هم چند گیگابایت است و قالبش را برنامه نمی‌خواند. Blender خودش سبک است (حدود ۳۰۰ مگابایت) و رایگان.

**با پنجره (دیداری):**
1. `courtyard-v12.blend` را در Blender باز کنید (نسخهٔ پشتیبان بگیرید).
2. در نمای سه‌بعدی شیءها را جابه‌جا، اضافه یا حذف کنید و ذخیره کنید.
3. بالای پنجره بروید به **Scripting**. در ویرایشگر متن **Open** بزنید و `tools\scene\export_garden.py` را باز کنید، سپس **Run Script** (▶). کنسول Blender (Window ← Toggle System Console) پیشرفت و در پایان اندازهٔ خروجی را می‌نویسد.
4. سربرگ خود اسکریپت می‌گوید از Text Editor با .blend باز هم کار می‌کند، ولی من آن را با پنجره نیازموده‌ام؛ اگر خطا داد، روش خط فرمان (مرحلهٔ ۳ در بخش پایین) مطمئن‌تر است و پنجرهٔ بازِ Blender را هم به هم نمی‌ریزد.

**بدون پنجره (خط فرمان):** همان دستور مرحلهٔ ۳ در بخش پایین.

برای خروجی دیگر: متغیر `MAZESTA_SCENE_OUT` مسیر دلخواه می‌دهد (پیش‌فرض `garden.mzscene` در پروژه).

## مراحل یک تغییر در صحنه

1. از `courtyard-v12.blend` یک نسخهٔ پشتیبان بگیرید (کپی با نام دیگر).
2. در Blender تغییر را بدهید و ذخیره کنید.
3. صادرکردن (بدون باز کردن پنجره):
   `blender --background "D:\DFM APp\Cloudy\Mazesta Test App Cloudy\Mazesta-Art\courtyard-v12.blend" --python tools/scene/export_garden.py`
   (از پوشهٔ `Mazesta`). خروجی `garden.mzscene` را جایگزین می‌کند.
4. اگر هندسه عوض شد، نور بازتاب‌شده دوباره محاسبه شود (کارت گرافیک با ray tracing لازم است):
   `$env:MAZESTA_BAKE_LIGHT="<مسیر کامل garden.light>"` و سپس
   `dotnet test tests/Mazesta.Diagnostics.Gpu.Tests -c Release --filter "FullyQualifiedName~Bakes_the_garden"`
   و دوباره build. تا این کار نشود یک آزمون خطا می‌دهد.
5. `dotnet build Mazesta.sln -c Release` و آزمون‌ها؛ سپس `pwsh tools/publish.ps1`.
6. **نسخهٔ بنچمارک را بالا ببرید** (`BenchmarkRecords.Headlines`)، چون بار کار عوض شده است.

## چطور سبک بماند

* **مثلث‌ها:** صحنه الان ۹٫۵ میلیون مثلث دارد و هزینهٔ هر فریم از مثلث‌هاست، نه از رزولوشن (`docs/SCENE-PERFORMANCE.md`). هر چیز تازه باید از همان سهمیه‌ها بگذرد: در `export_garden.py` جدول‌های `BUDGET` و `COPIES_BUDGET` سقف مثلث هر مش و هر گیاه را می‌گویند؛ چیزی که هزاران بار کپی می‌شود (برگ، گل) باید فقط شکل برگ داشته باشد. برای جزئیات، کارت‌های ساده با بافت بهتر از مدل پرمثلث‌اند.
* **نمونه‌سازی:** یک مش را چند بار کپی (instance) کنید، نه اینکه هر بار مش جدا بسازید؛ هر مش جدا یک draw call جدا و حافظهٔ جدا است.
* **Bevel و Subdivision:** فقط برای ساختمان (`KEEP_DETAIL`) فعال می‌ماند؛ روی بقیه خاموش می‌شود و نباید برای چیز دیگری روشنش کنید.
* **بافت:** حداکثر ۱۰۲۴ (نزدیک چشم) یا ۵۱۲؛ بیشتر از این حجم فایل و حافظهٔ کارت را بالا می‌برد.
* **گیاه تازه:** برای اینکه با باد (کمی) خم شود، نام شیء باید با یکی از پیشوندهای `SWAY` در `export_garden.py` شروع شود (`V10_SOURCE_Broadleaf`، `V9_SOURCE_Grass`، …) یا پیشوند تازه‌ای به آن جدول اضافه کنید. عددش میزان خم‌شدن است: درخت ۸ تا ۱۴، گل و علف ۷۰ تا ۱۵۰.
* **بعد از هر تغییر بسنجید:** اندازهٔ `garden.mzscene` (الان ۳۰ مگابایت)، شمار مثلث‌ها (آزمون‌های `GardenScene`) و زمان فریم (`MAZESTA_PROFILE=1` و آزمون‌های `GardenProfileHardwareTests`). اگر زمان فریم یا اندازه جهش کرد، تغییر را کوچک‌تر کنید.
* **پشتیبان:** گیت فقط کد و `garden.mzscene` را نگه می‌دارد. `Mazesta-Art` را جداگانه (دیسک دیگر یا ابر) کپی کنید؛ بدون آن نمی‌توان صحنه را دوباره صادر کرد.
