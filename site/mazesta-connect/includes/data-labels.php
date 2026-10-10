<?php
if (!defined('ABSPATH')) { exit; }
/** Persian words for the ids the app sends (reasons are the app's own Persian texts). */
return array(
    'reasons' => array(
        'Tuning_Out_StockUnstable' => 'کارت در همان حالت کارخانه خطا داد. تنظیم انجام نشد – ابتدا تست‌های کارت گرافیک را اجرا کنید.',
        'Tuning_Out_NoTelemetry' => 'درایور فرکانس یا توان این کارت را گزارش نمی‌کند، پس نتیجه قابل اندازه‌گیری نبود.',
        'Tuning_Out_NoCoreOffset' => 'این کارت یا درایور کنترل آفست هسته ندارد.',
        'Tuning_Out_ConfirmFailed' => 'هیچ تنظیمی از اجرای طولانی تأیید سربلند بیرون نیامد. چیزی ذخیره نشد.',
        'Tuning_Out_SlowerThanStock' => 'تنظیم پایدار از حالت کارخانه کندتر بود و نگه داشته نشد.',
        'Tuning_Out_UndervoltFound' => 'آندرولت پیدا شد: کارت همان بالاترین کلاک را با ولتاژ کمتر نگه می‌دارد، با عملکرد برابر یا بهتر و توان یا حرارت کمتر.',
        'Tuning_Out_NoSaving' => 'آفست پایدار پیدا شد اما توان یا دما را به‌طور قابل‌اندازه‌گیری کم نکرد و نگه داشته نشد.',
        'Tuning_Out_NoStableOffset' => 'حتی اولین قدم آفست ناپایدار بود؛ این کارت در فرکانس کارخانه حاشیه‌ای برای آندرولت ندارد.',
        'Tuning_Out_OverclockFound' => 'اورکلاک پیدا شد: به‌طور قابل‌اندازه‌گیری سریع‌تر از حالت کارخانه، بدون مصرف توان بیشتر.',
        'Tuning_Out_NoGain' => 'تنظیم پایدار به‌طور قابل‌اندازه‌گیری سریع‌تر از حالت کارخانه نبود و نگه داشته نشد.',
        'Tuning_Out_ApplyRefused' => 'درایور یکی از تنظیمات لازم برای جستجو را نپذیرفت',
        'Tuning_Out_NoResult' => 'جستجو بدون نتیجه تمام شد.',
        'Tuning_Out_Cancelled' => 'متوقف شد. کارت به حالت کارخانه برگشت؛ چیزی ذخیره نشد.',
        'Tuning_Out_NoPowerLimit' => 'درایور این کارت حد توانی برای بالا بردن نمی‌دهد، پس اورکلاک پلاس چیزی بالاتر از سقف کارخانه ندارد.',
    ),
    'outcomes' => array('Passed' => 'سالم', 'Failed' => 'ایراد', 'Error' => 'خطای تست', 'Unsupported' => 'پشتیبانی نمی‌شود', 'Cancelled' => 'لغو شد', 'Inconclusive' => 'نامشخص', 'Completed' => 'کامل شد', 'Incomplete' => 'ناقص',
        'Improved' => 'بهبود', 'NoImprovement' => 'بدون بهبود', 'Failed ' => 'ناموفق'),
    'tuning' => array('Undervolt' => 'آندرولت', 'Overclock' => 'اورکلاک', 'OverclockPlus' => 'اورکلاک پلاس'),
    'tests' => array(
        'cpu.stress' => 'پردازنده: فشار کامل', 'cpu.fft' => 'پردازنده: FFT', 'cpu.hash' => 'پردازنده: هش', 'cpu.integer' => 'پردازنده: اعداد صحیح', 'cpu.linpack' => 'پردازنده: Linpack',
        'cpu.matrix' => 'پردازنده: ماتریس', 'cpu.singlecore' => 'پردازنده: تک‌هسته', 'cpu.vector' => 'پردازنده: برداری', 'memory.bitfade' => 'رم: Bit-fade', 'memory.pattern' => 'رم: الگو',
        'network.lan' => 'شبکه: LAN', 'network.latency' => 'شبکه: تأخیر', 'network.speed' => 'شبکه: سرعت', 'storage.random4k' => 'ذخیره‌ساز: تصادفی ۴K', 'storage.sequential' => 'ذخیره‌ساز: ترتیبی',
        'storage.smart' => 'ذخیره‌ساز: SMART', 'windows.dism' => 'ویندوز: DISM', 'windows.sfc' => 'ویندوز: SFC', 'power.combined' => 'توان: ترکیبی',
        'gpu.variable' => 'گرافیک: بار متغیر', 'gpu.pulse' => 'گرافیک: ضربه‌ای', 'gpu.vram' => 'گرافیک: VRAM', 'gpu.render' => 'گرافیک: رندر', 'gpu.ai' => 'گرافیک: هوش مصنوعی',
        'bench.cpu.single' => 'بنچ: پردازنده تک‌هسته', 'bench.cpu.multi' => 'بنچ: پردازنده چندهسته', 'bench.memory' => 'بنچ: رم', 'bench.storage' => 'بنچ: ذخیره‌ساز',
        'bench.gpu.rt' => 'بنچ: ردیابی پرتو', 'bench.gpu.ai' => 'بنچ: هوش مصنوعی کارت', 'bench.gpu.scene.d3d' => 'بنچ: صحنهٔ سه‌بعدی', 'bench.network.internet' => 'بنچ: اینترنت', 'bench.ai.llm' => 'بنچ: مدل زبانی',
    ),
    /* the headline figure of a benchmark: the metric key, its unit and a short name */
    'headline' => array(
        'bench.cpu.single' => array('Bench_Cpu_Gflops', 'GFLOPS'), 'bench.cpu.multi' => array('Bench_Cpu_Gflops', 'GFLOPS'), 'bench.memory' => array('Bench_Mem_Read', 'GB/s'),
        'bench.storage' => array('Bench_Storage_SeqRead', 'MB/s'), 'bench.gpu.rt' => array('Bench_Gpu_Rt_Fps', 'FPS'), 'bench.gpu.ai' => array('Bench_Gpu_Ai_Fp32', ''), 'bench.ai.llm' => array('Bench_Ai_Gen', 'tok/s'),
        'bench.gpu.scene.d3d' => array('Bench_Scene_ScoreGpu', 'امتیاز'), 'bench.network.internet' => array('Bench_Net_Download', 'Mbps'),
    ),
);
