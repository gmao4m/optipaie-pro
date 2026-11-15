# 07 — المسار عند أوّل فتح بعد التحديث / Première ouverture après mise à jour

> **المشكلة التي يحلّها هذا المسار.** ‏300 تثبيت حيّ لدى الزبائن أُنشئت قبل نظام الصلاحيات.
> مع تفويض «يفشل مغلقًا» وبلا ترحيل، كان الخطر أن يُحجب كلّ واحد من هؤلاء عن بياناته هو عند أوّل
> فتح. هذا المسار يسدّ تلك الثغرة: يُثبّت **المالك** عند أوّل فتح — **دون أن يمسّ البيانات إطلاقًا**.
>
> **Le risque couvert.** 300 installations en service datent d'avant le système de permissions.
> Avec une autorisation « fail-closed » et sans migration, chacune risquait d'être verrouillée hors
> de ses propres données. Ce chemin établit le **propriétaire** à la première ouverture — **sans
> jamais toucher aux données**.

## ما يراه الزبون / Ce que voit le client

### الحالة ١ — تثبيت مُفعَّل له حساب (الحالة الشائعة)
التطبيق يتعرّف على الحساب الموجود ويعتمده **مالكًا** في مكانه، **بصمت**. لا شاشة، لا سؤال.
كلمة السرّ **تبقى كما هي**: الزبون يدخل بنفس كلمة السرّ التي كان يستعملها. تُرحَّل البيانات أبدًا.

**Cas 1 — installation activée, avec un compte (le cas courant).** L'application reconnaît le
compte existant et l'adopte **en place** comme **propriétaire**, **silencieusement**. Aucun écran,
aucune question. Le mot de passe est **conservé** : le client se connecte avec celui qu'il avait
déjà. Les données ne sont jamais migrées.

### الحالة ٢ — فيه بيانات لكن بلا حساب (تثبيت قديم جدًّا)
يُعرض إعداد لمرّة واحدة يشرح — بالعربية والفرنسية — أنّ الحسابات أصبحت مطلوبة، ويقود الزبون
لإنشاء **حساب المالك** (بريد + مؤسسة + كلمة سرّ ≥ 10 محارف). لا شيء آخر يُفتح قبل إتمام ذلك.
عند الإنشاء يُعرض **رمز الاسترجاع لمرّة واحدة** كبيرًا، قابلًا للنسخ والطباعة، مع خانة تأكيد الحفظ
قبل المتابعة. **لا تُمسّ البيانات الموجودة.**

**Cas 2 — des données mais aucun compte (installation très ancienne).** Un assistant unique
explique — en arabe et en français — que les comptes sont désormais requis, et guide le client pour
créer le **compte propriétaire** (e-mail + entreprise + mot de passe ≥ 10 caractères). Rien d'autre
n'est accessible tant que ce n'est pas fait. À la création, le **code de récupération à usage
unique** s'affiche en grand, copiable et imprimable, avec une case à cocher confirmant qu'il a été
enregistré avant de continuer. **Les données existantes ne sont pas touchées.**

### الحالة ٣ — تثبيت فارغ (تجربة / تثبيت جديد)
لا شيء من هذا يظهر. المالك يُنشأ في المسار العادي (التفعيل / التسجيل). التجربة لا تُجبَر على
إنشاء حساب.

**Cas 3 — installation vide (essai / nouvelle installation).** Rien de tout cela ne s'affiche. Le
propriétaire est créé par le flux normal (activation / inscription). L'essai n'est jamais forcé.

## الضمانات / Les garanties

1. **البيانات لا تُمسّ أبدًا** — لا تُعاد مَفْتَحتها، لا تُرحَّل، لا يُعاد حسابها. ترحيلات المخطّط
   (0037..0039) إضافية فقط (قيم افتراضية)، تُطبَّق في نفس الفتحة الأولى وتُبقي كلّ صفّ كما هو.
2. **عند الفشل، التطبيق لا يفتح بعرض فارغ أو مقيَّد** للبيانات الموجودة: يُظهر رسالة خطأ ثنائية
   اللغة ويترك قاعدة البيانات **كما كانت تمامًا**، ثمّ يُغلق. (`App.EnsureOwnerEstablished` يعيد
   `false` ⇒ خروج نظيف، بلا شلّ.)
3. **الحساب الموجود يُرحَّل إلى مالك، لا يُحذف**، وكلمة سرّه محفوظة — يدخل بما كان يملك.
4. **الترتيب**: يعمل هذا المسار **بعد** بوّابة التفعيل (فيوجد حساب حديث التفعيل ليُعتمَد) و**قبل**
   بذر بيانات العرض (فلا تُجبَر تجربةٌ على إنشاء مالك).

1–4. **Les données ne sont jamais touchées** ; en cas d'échec l'app **n'ouvre pas** une vue vide ou
restreinte — message bilingue, base **inchangée**, puis fermeture ; le compte existant est
**adopté** (jamais supprimé), mot de passe conservé ; l'ordre place ce chemin **après** la porte
d'activation et **avant** le jeu de démonstration.

## أين في الكود / Où dans le code
- القرار الصافي: [`OwnerSetupPolicy`](../../src/OptiPaie.Services/Startup/OwnerSetupPolicy.cs) — يصنّف
  التثبيت (قراءة فقط).
- التنسيق: [`OwnerSetupService`](../../src/OptiPaie.Services/Startup/OwnerSetupService.cs) — يعتمد/ينشئ
  المالك، يُصدر رمز الاسترجاع، يرقّعه عند الغياب؛ لا يمسّ البيانات.
- الربط: [`App.EnsureOwnerEstablished`](../../src/OptiPaie.Desktop/App.xaml.cs) + شاشة
  [`OwnerSetupWindow`](../../src/OptiPaie.Desktop/Views/OwnerSetupWindow.xaml).

## الإثبات / La preuve
[`PrePermissionUpgradeTests`](../../tests/OptiPaie.Tests/PrePermissionUpgradeTests.cs) يبني قاعدة
**حقيقية مجمَّدة على المخطّط المنشور (v36)** من نفس سكربتات الترحيل المضمَّنة، يملؤها بشركة وموظّفين
وعقود وكشوف، ثمّ يُجري **نفس التحديث الذي يُجريه تطبيق الزبون**: `MigrationRunner` الحقيقي
(0037..0039) ثمّ `OwnerSetupService`. يؤكّد الضمانات الثلاث: **البيانات كلّها باقية**، **المالك يدخل**،
و**كلّ وحدة يجب أن يراها المالك قابلة للفتح** — في الشكلين (حساب موجود يُعتمد / لا حساب فيُنشأ).

## الحدّ من الصلاحيات / Enforcement
تثبيت المالك لا يُشغّل بوّابة الدخول متعدّد المستخدمين (`Auth.Enabled`) ولا يُفعّل الحدّ على الأدوار
من تلقاء نفسه — فالمالك له كلّ الصلاحيات أصلًا، ولا مستخدمين محليّين محدودي الدور بعد. يبدأ الحدّ
فعليًّا حين يُنشئ المالك مستخدمين محليّين بأدوار ويُشغّل البوّابة (شاشة المستخدمين). هذا يحفظ سلوك
التثبيتات الحالية كما هو، ويمنع أي حجب غير مقصود.
