# Pharmacy System | نظام إدارة الصيدلية

نظام إدارة صيدلية واحدة، ثنائي اللغة (عربي RTL / English LTR)، يعمل على الشبكة المحلية.
A single-pharmacy management system, fully bilingual (Arabic RTL / English LTR), built for a local network.

> **الحالة / Status:** المزايا كلها منفّذة ومختبَرة آلياً، وهو بانتظار اختبار القبول اليدوي في صيدلية حقيقية (`docs/uat-scenarios.md`).
> All features are implemented and covered by automated tests; manual acceptance testing in a real pharmacy is still pending.

## المزايا | Features

| الوحدة | ما تقدمه |
|---|---|
| **المبيعات** | فواتير بيع بالشريط أو العلبة، صرف الأقرب انتهاءً أولاً (FEFO)، خصم على الفاتورة بسقف قابل للضبط، موافقة مشرف (خصم كبير، بيع تحت التكلفة)، طباعة A4 وإيصال 80 ملم |
| **المخزون** | دفعات وتواريخ صلاحية وتكلفة، حجر (Quarantine)، إتلاف وتسويات بسبب إلزامي، لا مخزون سالب أبداً |
| **المشتريات والموردون** | فواتير شراء نقدية أو آجلة، كشف حساب مورد، سداد، مرتجعات شراء |
| **العملاء والديون** | كشف حساب، تحصيل، رصيد افتتاحي، مرتجعات مبيعات بقاعدة استرداد موحّدة (الدين أولاً ثم النقد) |
| **الخزينة** | ورديات كاشير، إغلاق بجرد النقد وتسجيل العجز والزيادة، حركات يدوية |
| **المصروفات** | فئات ومصروفات من الوردية، مع إلغاء بسبب |
| **التقارير** | 19 تقريراً (مبيعات، مشتريات، مخزون، صلاحيات، أرباح، كشوف حسابات…) مع تصدير Excel وPDF وطباعة |
| **لوحة التحكم** | مؤشرات اليوم والشهر، مخطط 7 أيام، تنبيهات المنتهي وقريب الانتهاء والمنخفض |
| **استيراد Excel** | المنتجات والمخزون الافتتاحي مع فحص الأخطاء قبل الحفظ |
| **الأمان** | صلاحيات دقيقة لكل دور، سجل تدقيق، ترويسات أمان، فحص مطابقة الحسابات |

## التقنيات | Tech stack
- **ASP.NET Core 10** · C# · **Blazor Server** + **MudBlazor**
- **Entity Framework Core** · **SQL Server**
- **ASP.NET Identity** (صلاحيات كـ Claims وسياسات)
- **Serilog** · **ClosedXML** (Excel) · **QuestPDF** (PDF)
- **xUnit** · **Shouldly** · **NetArchTest** · WebApplicationFactory (مع SQL Server حقيقي)
- معمارية **Modular Monolith** بطبقات نظيفة: `Domain` ← `Application` ← `Infrastructure` ← `Web`

## التشغيل للتطوير | Run for development
المتطلبات: **.NET SDK 10** (انظر `global.json`) و**SQL Server** محلي (Express يكفي).

```powershell
dotnet run --project src/Pharmacy.Web --launch-profile http
```
- يستخدم الاتصال `PharmacyDb_Dev` على `Server=.` (`appsettings.Development.json`) ويُنشئ القاعدة ويرقّيها تلقائياً في بيئة التطوير.
- عند أول تشغيل تظهر صفحة **الإعداد الأولي** لإنشاء حساب المدير.
- افتح `http://localhost:5016`.

## الاختبارات | Tests
```powershell
dotnet test
```
أكثر من 750 اختباراً: وحدات الدومين، معمارية الطبقات، ترجمة كل المفاتيح، وتكامل مع SQL Server حقيقي (تزامن، مطابقة الدفاتر، الصلاحيات، التقارير، الاستيراد). لكل اختبار تكامل قاعدة بيانات مؤقتة تُنشأ وتُحذف. يمكن تحديد الخادم بالمتغير `PHARMACY_TEST_SQL_SERVER`.

## التوثيق والنشر | Docs & deployment
- [`docs/installation-guide.md`](docs/installation-guide.md): التثبيت على الشبكة المحلية (HTTPS، خدمة Windows)
- [`docs/backup-and-restore.md`](docs/backup-and-restore.md): النسخ الاحتياطي والاسترجاع
- [`docs/import-guide.md`](docs/import-guide.md): استيراد المنتجات والمخزون من Excel
- [`docs/uat-scenarios.md`](docs/uat-scenarios.md): سيناريوهات اختبار القبول
- [`docs/security-review.md`](docs/security-review.md) · [`docs/performance-report.md`](docs/performance-report.md)
- [`deploy/`](deploy): سكربتات التثبيت كخدمة والنسخ الاحتياطي والاسترجاع

## كيف بُني | How it was built
بُني على 14 مهمة متتابعة، لكل منها: تحليل ← خطة ← موافقة ← تنفيذ ← اختبار ← مراجعة. القواعد المالية (FEFO، الخصم، المرتجع، الربح) دوال نقية تُختبر وحدها، وتتحقق اختبارات التزامن من عدم تكرار العمليات أو تلف الأرصدة. استُخدم **Claude Code** مساعداً في التحليل والكتابة والمراجعة.

Built in 14 sequential tasks, each going through analysis, plan, approval, implementation, tests and review. AI-assisted with Claude Code.

## الترخيص | License
لم يُحدَّد ترخيص بعد؛ حقوق الاستخدام والنسخ محفوظة لصاحب المشروع إلى أن يُضاف ملف `LICENSE`.
No license has been chosen yet; all rights are reserved until a `LICENSE` file is added.
