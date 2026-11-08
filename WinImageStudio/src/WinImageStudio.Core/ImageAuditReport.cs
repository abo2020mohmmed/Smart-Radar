using System.Net;
using System.Text;

namespace WinImageStudio.Core;

public static class ImageAuditReport
{
    public static string Html(ProjectSession project, Inventory inventory)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
        var html = new StringBuilder("<!doctype html><html lang='ar' dir='rtl'><meta charset='utf-8'><title>فحص صورة Windows</title><style>body{font:16px system-ui;background:#0d1117;color:#e6edf3;margin:32px}table{border-collapse:collapse;width:100%;margin:12px 0 30px}td,th{border:1px solid #344154;padding:9px;text-align:start;overflow-wrap:anywhere}th{background:#26364a}p{line-height:1.8}h2{color:#65d6a6}</style><h1>فحص صورة Windows المحفوظة</h1>");
        html.Append("<p>المشروع: ").Append(E(project.Id)).Append("<br>الصورة المفحوصة: ").Append(E(project.WorkingWim)).Append("<br>وقت الفحص UTC: ").Append(E(DateTimeOffset.UtcNow.ToString("u"))).Append("</p>");
        html.Append("<p>هذا جرد غير متصل لصورة المشروع المحفوظة، وليس فحصًا أمنيًا أو اختبار إقلاع. لا يضمن اكتشاف البرامج المحمولة أو برامج كل المستخدمين. برامج SetupComplete ملفات مضافة للتثبيت لاحقًا، وليست مثبتة بالضرورة. إذا تغير المشروع بعد بناء ISO، أعد البناء لتطابقه مع هذا التقرير.</p>");
        if (inventory.Profile is { } p)
            html.Append("<p>الإصدار: ").Append(E(p.EditionId)).Append(" | البناء: ").Append(E(p.Build)).Append("<br>لغات الواجهة: ").Append(E(string.Join(", ", p.InstalledLanguages)))
                .Append("<br>Defender بحسب المحرك/الخدمة: ").Append(p.DefenderPresent ? "موجود" : "لم يُكتشف").Append("<br>OneDrive بحسب الملفات/الحزم: ").Append(p.OneDrivePresent ? "ما زالت عناصر موجودة" : "لم تُكتشف عناصر").Append("</p>");
        void Table(string title, IReadOnlyList<InventoryItem>? rows)
        {
            html.Append("<h2>").Append(E(title)).Append(" (").Append(rows?.Count ?? 0).Append(")</h2><table><tr><th>الاسم</th><th>الحالة</th><th>التفاصيل</th></tr>");
            if (rows is null) html.Append("<tr><td colspan='3'>لم تُجمع هذه البيانات؛ لا يعني ذلك غياب البرامج.</td></tr>");
            else if (rows.Count == 0) html.Append("<tr><td colspan='3'>لم تُكتشف عناصر في هذه الفئة.</td></tr>");
            else foreach (var row in rows) html.Append("<tr><td>").Append(E(row.Name)).Append("</td><td>").Append(E(row.State)).Append("</td><td>").Append(E(row.Details)).Append("</td></tr>");
            html.Append("</table>");
        }
        Table("تطبيقات Windows المجهزة Appx", inventory.Apps);
        Table("برامج Win32 المسجلة للصورة · HKLM فقط", inventory.Win32Programs);
        Table("ملفات تثبيت مضافة للتشغيل لاحقًا", inventory.StagedInstallers);
        Table("ميزات Windows", inventory.Features); Table("حزم النظام والتحديثات", inventory.Packages);
        Table("التعريفات", inventory.Drivers); Table("الخدمات · 2 تلقائي، 3 يدوي، 4 معطل", inventory.Services);
        Table("الإمكانات واللغات", inventory.Capabilities);
        return html.Append("</html>").ToString();
    }
}
