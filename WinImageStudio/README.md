# WinImageStudio — نسخة Windows

مشروع WPF عربي لتخصيص صور Windows، مستقل عن ملفات Smart-Radar الأخرى.

## تنزيل البرنامج دون بناء يدوي

1. افتح [صفحة بناء Windows](https://github.com/abo2020mohmmed/Smart-Radar/actions/workflows/winimage-studio.yml).
2. افتح آخر تشغيل ناجح بعلامة خضراء.
3. من Artifacts نزّل **WinImageStudio-windows-x64**. يلزم تسجيل الدخول إلى GitHub للتنزيل.
4. فك الضغط كاملًا، ثم شغّل **Start-WinImageStudio.cmd** أو **WinImageStudio.exe** ووافق على صلاحيات Administrator.

النسخة ذاتية الاحتواء ولا تحتاج تثبيت .NET أو Visual Studio. تحتاج أدوات Windows ADK/oscdimg لبناء ISO، وwimlib لجمع SWM. ملف QUICKSTART.ar.txt داخل الحزمة يشرح التشغيل. حزم Actions تبقى 30 يومًا؛ يمكن إعادة البناء من Run workflow لاحقًا.


## جديد: التخفيف القوي لـPro

بعد تركيب الصورة افتح التبويب السادس، حلّل المكونات، ثم أضف الخطة وراجعها وطبّقها من التبويب الخامس. يُحافظ على المتجر والطباعة والعربية والإنجليزية المثبتتين، ويُختار Xbox وOneDrive والتطبيقات الإضافية من قائمة محددة. Defender المحمي قد يبقى؛ التقرير يوضح ذلك ولا يدّعي حذفه بالكامل. [الدليل الكامل والحدود](docs/STRONG-PRESET.ar.md).

## المصدر والتوثيق

- [الدليل العربي الكامل](README.ar.md)
- [نتائج التحقق المحلي وحدوده](docs/VERIFICATION.md)
- [اختبارات القبول على Windows](docs/ACCEPTANCE.md)
- [البنية وحدود الثقة](docs/ARCHITECTURE.md)

المشروع إصدار هندسي أولي 0.2.0، وليس بديلًا مطابقًا لجميع قدرات NTLite. الاختبارات الآلية لا تثبت أن صورة Windows المعدلة ستقلع أو تتحدث بنجاح. اختبر الناتج في جهاز افتراضي أولًا.
