namespace WinImageStudio.Core;

public enum WizardPage { Source, Prepare, Customize, Review, Output, Done }
public enum WizardAction { None, Create, Resume, Prepare, Analyze, Review, Apply, Approve, Commit, BuildIso, Finish, OpenFolder }
public sealed record WizardDecision(WizardAction Action, string Caption, string Hint, bool Enabled = true);

public static class WizardFlow
{
    public static WizardDecision Next(WizardPage page, ProjectSession? project, int pendingCount, bool strong,
        bool analysisReady, bool canApprove, bool acknowledged, bool sourceReady, bool isoReady)
    {
        if (project?.State == ImageState.RecoveryRequired)
            return new(WizardAction.None, "استعادة الجلسة مطلوبة", "استخدم زر استعادة الجلسة في هذه الصفحة؛ ستُتجاهل التعديلات غير المحفوظة.", false);
        if (page == WizardPage.Source)
            return project is not null ? new(WizardAction.Resume, "التالي — متابعة المشروع", "المتابعة من آخر مرحلة محفوظة.") :
                new(WizardAction.Create, "التالي — قراءة المصدر", "اختر ملف Windows ومجلد العمل ثم اضغط التالي.", sourceReady);
        if (project is null) return new(WizardAction.None, "اختر المصدر أولًا", "ابدأ من صفحة المصدر.", false);
        if (page == WizardPage.Done) return new(WizardAction.OpenFolder, "فتح مجلد النتيجة", "حُفظت النتيجة. اختبر النسخة في جهاز افتراضي.");
        if (page == WizardPage.Output)
        {
            if (project.State != ImageState.Committed) return new(WizardAction.None, "احفظ الصورة أولًا", "لا يمكن إنشاء ISO قبل اكتمال حفظ الصورة.", false);
            return project.MediaPath is null ? new(WizardAction.Finish, "إنهاء — صورة WIM محفوظة", "المصدر ليس ISO كاملًا؛ حُفظت صورة WIM ويمكنك فتح مجلدها.") :
                new(WizardAction.BuildIso, "التالي — إنشاء ISO", "حدد اسم ملف جديد وأداة oscdimg ثم اضغط التالي لبدء الإنشاء.", isoReady);
        }
        if (project.State == ImageState.Committed) return new(WizardAction.Resume, "التالي — الإخراج", "الصورة محفوظة؛ انتقل إلى إنشاء ISO.");
        if (page == WizardPage.Prepare) return new(WizardAction.Prepare, "التالي — تجهيز الصورة", "سيُنسخ الإصدار ويُركّب، ثم يُحلل تلقائيًا في المسار القوي.");
        if (project.State != ImageState.Mounted) return new(WizardAction.None, "جهّز الصورة أولًا", "ارجع إلى صفحة الإصدار والتركيب.", false);
        if (page == WizardPage.Customize)
            return strong && !analysisReady ? new(WizardAction.Analyze, "التالي — تحليل التعديلات", "تغيرت الخيارات؛ حلّل الصورة ثم راجع القائمة الجديدة.") :
                new(WizardAction.Review, "التالي — مراجعة الخطة", "ستظهر الخطة كاملة قبل تطبيق أي تغيير.");
        if (WorkflowRules.CanCommit(project, pendingCount))
            return new(WizardAction.Commit, "التالي — حفظ الصورة", "سيحفظ هذا الزر التعديلات ويلغي تركيب الصورة، ثم يفتح صفحة الإخراج.");
        if (pendingCount == 0 && !canApprove)
            return new(WizardAction.Analyze, "التالي — تحليل الصورة", "الخطة فارغة ولم تُطبق بعد؛ أعد التحليل لمراجعة التعديلات.");
        return new(pendingCount > 0 ? WizardAction.Apply : WizardAction.Approve,
            pendingCount > 0 ? "التالي — تطبيق التعديلات" : "التالي — اعتماد الخطة الفارغة",
            acknowledged ? "بعد نجاح التطبيق راجع النتيجة ثم اضغط التالي للحفظ." : "راجع القائمة وحدد مربع الموافقة لتفعيل زر التالي.", acknowledged);
    }
}
