# الإصدار 1.39.0 — لوحة قيادة جديدة، وتصحيحات شهادة العمل والأجر

## لوحة القيادة — إعادة تصميم كاملة
- **ترتيب أوضح للمعلومات**: الأهمّ أوّلاً (الكتلة الأجرية، التعداد، ما ينتظر المعالجة)، ثمّ التحاليل، ثمّ النشاط الأخير.
- **قسم الموارد البشرية (التعداد)**: مؤشّرات محسوبة تلقائيًا من قاعدة الموظفين — التعداد، متوسّط العمر، الأقدمية، التوزيع حسب المنصب والقسم والجنس ونوع العقد، **ومؤشّر بلوغ سنّ التقاعد** (خلال 12 شهرًا، مع الحالات المتجاوَزة).
- **كلّ رقم قابل للنقر** ويفتح القائمة الاسمية التي تكوّنه.
- **منحنى الكتلة الأجرية صادق**: يُظهر الاتّجاه الحقيقي دون تجميل، مع تسمية واضحة للفترة.
- **أسرع** حتى مع عدد كبير من الموظفين.
- **عربية كاملة (RTL)** وترتيب صحيح للتواريخ في الاتّجاهين.

## شهادة العمل والأجر (CNAS — AS.08) — تصحيحان أبلغ عنهما العملاء
- **خانات تواريخ التوقّف عن العمل لم تعُد تُملأ تلقائيًا** في الشهادة العادية: «تاريخ آخر يوم عمل» و«تاريخ استئناف العمل» تخصّان حالة التوقّف عن العمل (مرض، أمومة، حادث، عجز) فقط، وتبقى **فارغة** في الشهادة العادية لتُملأ يدويًا عند الحاجة. وأُضيف خيار صريح «شهادة توقّف عن العمل» لمن يحتاجه.
- **جدول الأجور (الصفحة 2) يعرض الآن البيانات الحقيقية** بدل الشرطة «/»: الأجر الخاضع للاشتراك، وحصّة العامل (9%)، وعدد أيام العمل — مأخوذة من **سجلّ الأجور الفعلي**، وبنفس الأساس الخاضع للاشتراك الذي تعتمده تصريحات CNAS دون إعادة حساب. أمّا الأشهر غير المعنيّة فتبقى وحدها مشطوبة «/».

## وحدات أخرى
- **نوع عطلة جديد: «عطلة الاسترجاع»** أُضيف إلى قائمة أنواع العطل (مدفوعة، ولا تُخصم من الرصيد السنوي).
- **التوظيف**: يُفتح عرضُ العمل للتعديل الآن **بالنقر المزدوج**، كبقيّة القوائم.

## القواعد
- **محرّك الأجور لم يُمَسّ**، ولا أيّ هجرة تكسر قواعد بياناتكم الحالية.
- **عزل صارم لكلّ شركة**، واختبارات عدم ارتداد على كلّ رقم جديد.
- قواعد بياناتكم الحالية تُحدَّث تلقائيًا دون فقدان.

---

# Version 1.39.0 — Nouveau tableau de bord + corrections de l'attestation de travail et de salaire

## Tableau de bord — refonte complète
- **Hiérarchie plus claire** : l'essentiel d'abord (masse salariale, effectif, à traiter), puis les analyses, puis l'activité récente.
- **Section Ressources humaines (effectif)** : indicateurs calculés automatiquement depuis la base des employés — effectif, âge moyen, ancienneté, répartition par poste / département / genre / contrat, et **indicateur de départ en retraite** (dans les 12 mois + cas déjà atteints).
- **Chaque chiffre est cliquable** et ouvre la liste nominative qui le compose.
- **Courbe de masse salariale honnête** : la vraie tendance, sans embellissement, avec une période clairement libellée.
- **Plus rapide**, même sur un grand effectif.
- **Arabe complet (RTL)** et ordre des dates correct dans les deux sens.

## Attestation de travail et de salaire (CNAS — AS.08) — deux corrections signalées par des clients
- **Les dates d'arrêt de travail ne sont plus pré-remplies** sur une attestation ordinaire : « dernier jour de travail » et « reprise du travail » ne concernent qu'un arrêt (maladie, maternité, accident, invalidité) et restent **vides** pour être complétées à la main si besoin. Une case explicite « arrêt de travail » a été ajoutée.
- **Le tableau des salaires (page 2) affiche désormais les vraies données** au lieu du « / » : salaire soumis à cotisation, part ouvrière (9 %) et jours travaillés, repris de **l'historique de paie réel** — la même assiette que les déclarations CNAS, sans recalcul. Seuls les mois non concernés restent barrés « / ».

## Autres modules
- **Nouveau type de congé : « Congé de récupération »** ajouté à la liste (payé, ne décompte pas le solde annuel).
- **Recrutement** : une offre d'emploi s'ouvre maintenant en **double-clic**, comme toutes les autres listes.

## Les règles
- **Moteur de paie non touché**, aucune migration qui casse vos bases.
- **Isolation stricte par société**, tests de non-régression sur chaque nouveau chiffre.
- Vos bases existantes sont mises à jour automatiquement, sans perte.
