using System;
using UnityEngine;

// All the tuning values of the car, grouped. Each group is a foldout in the Inspector of CarController.
// (Hover over a value in the Inspector to see the explanation.)

public enum DriveMode { AllWheel, RearWheel }

[Serializable]
public class BodySettings
{
    [Tooltip("משקל הרכב בק\"ג. כל הכוחות מחושבים ביחס למשקל, אז שינוי כאן לא משנה את התחושה - רק את היחס לחפצים שהוא פוגע בהם.")]
    public float mass = 20f;

    [Tooltip("מרכז הכובד ביחס לנקודת המרכז של הרכב (גובה ציר הגלגלים). ערך שלילי = נמוך יותר = הרכב מתהפך פחות.")]
    public Vector3 centerOfMass = new Vector3(0f, -0.15f, 0f);

    [Tooltip("כמה קשה לסובב את הרכב. גבוה = יציב ועצל יותר, נמוך = מסתובב ומתהפך מהר.")]
    public float inertiaMultiplier = 1.5f;

    [Tooltip("התנגדות אוויר כללית. 0 = אין.")]
    public float linearDrag = 0.02f;

    [Tooltip("בלימה של סיבובים בכל הכיוונים (גלגול והטיה). גבוה = הרכב מפסיק להתנדנד ולהסתובב מהר יותר.")]
    public float angularDrag = 1.0f;

    [Tooltip("כוח המשיכה ביחס לרגיל. 1 = כמו בעולם האמיתי, 2 = רכב צעצוע כבד ודביק שנוחת מהר. (אל תפחית מתחת ל-1.)")]
    public float gravityScale = 2f;

    [Tooltip("מהירות הסיבוב המקסימלית (רדיאנים בשנייה). מונע סיבובים משוגעים אחרי התנגשות.")]
    public float maxAngularSpeed = 20f;

    [Tooltip("כמה חישובי פיזיקה לכל צעד. יותר = יציב יותר אבל כבד יותר.")]
    public int solverIterations = 12;

    [Tooltip("אם מסומן, המשחק מגדיר בעצמו צעד פיזיקה של 0.01 שנייה (100 פעמים בשנייה) כשהוא מתחיל.")]
    public bool setFastPhysicsStep = true;
}

[Serializable]
public class SuspensionSettings
{
    [Tooltip("כמה הגלגל יכול לרדת ולעלות (במטרים), מהמצב העליון ועד המצב התלוי באוויר. גדול = רכב שטח עם קפיצים ארוכים.")]
    public float travel = 0.4f;

    [Tooltip("חוזק הקפיצים. גבוה = רכב קשיח שלא שוקע. נמוך = רכב רך ומתנדנד. הרכב שוקע בערך (כוח משיכה) חלקי המספר הזה.")]
    public float springStrength = 110f;

    [Tooltip("בלימת הקפיצים (בולמי זעזועים). נמוך = קופץ הרבה אחרי נחיתה. גבוה = נעצר מהר ונעשה קשיח.")]
    public float damping = 10f;

    [Tooltip("מוט מייצב: מונע מהרכב להישען חזק בפניות. 0 = אין. גבוה = הרכב נשאר ישר אבל פחות \"חי\".")]
    public float antiRoll = 40f;

    [Tooltip("מכפיל לרדיוס הגלגל שמשמש לחישוב מגע עם הקרקע. 1 = בדיוק כמו הגלגל.")]
    public float wheelRadiusScale = 1f;
}

[Serializable]
public class DriveSettings
{
    [Tooltip("איזה גלגלים מקבלים כוח מהמנוע. AllWheel = כל 4 (יציב). RearWheel = רק אחוריים (מחליק יותר, כיף יותר).")]
    public DriveMode driveMode = DriveMode.AllWheel;

    [Tooltip("כמה חזק הרכב מאיץ (מטר בשנייה בריבוע). גבוה = האצה אגרסיבית.")]
    public float acceleration = 30f;

    [Tooltip("מהירות מקסימלית קדימה (מטר בשנייה). 28 זה בערך 100 קמ\"ש.")]
    public float maxSpeed = 28f;

    [Tooltip("מהירות מקסימלית בנסיעה אחורה.")]
    public float maxReverseSpeed = 10f;

    [Tooltip("כוח הבלימה כשלוחצים הפוך לכיוון הנסיעה.")]
    public float brakeStrength = 45f;

    [Tooltip("האטה טבעית כשמרפים מהגז.")]
    public float coastDrag = 3f;

    [Tooltip("בלם היד (רווח): כמה חזק הגלגלים האחוריים בולמים.")]
    public float handbrakeStrength = 12f;

    [Tooltip("גובה (במטרים) מעל הקרקע שבו פועל כוח ההנעה. נמוך = הרכב מרים את האף בהאצה ומטה אותו בבלימה (מעוררת תחושת משקל). גבוה = יציב וישר יותר.")]
    public float forceHeight = 0f;

    [Tooltip("לחץ כלפי מטה שגדל עם המהירות. עוזר לאחיזה ולציפה בקטעים מהירים.")]
    public float downforce = 0.3f;
}

[Serializable]
public class SteeringSettings
{
    [Tooltip("זווית ההיגוי המקסימלית של הגלגלים הקדמיים (מעלות) במהירות נמוכה.")]
    public float maxSteerAngle = 32f;

    [Tooltip("זווית ההיגוי המקסימלית במהירות גבוהה. קטנה יותר = יציב יותר במהירות.")]
    public float highSpeedSteerAngle = 8f;

    [Tooltip("המהירות (מטר בשנייה) שבה ההיגוי מגיע לערך של מהירות גבוהה.")]
    public float highSpeedReference = 26f;

    [Tooltip("כמה מהר הגלגלים פונים לכיוון ההיגוי.")]
    public float steerResponse = 9f;

    [Tooltip("כמה מהר הגלגלים חוזרים ישר כשעוזבים את החצים.")]
    public float steerReturn = 14f;

    [Tooltip("היגוי חד יותר בזמן בלם יד. 1 = כרגיל.")]
    public float driftSteerBoost = 1.1f;
}

[Serializable]
public class GripSettings
{
    [Tooltip("כמה חזק הצמיגים נדבקים לכביש (מקדם חיכוך). גבוה = אחיזה חזקה מאוד. נמוך = הרכב מחליק.")]
    public float friction = 1.8f;

    [Tooltip("כמה מהר הצמיגים מתקנים החלקה הצידה (0 עד 1). גבוה = אחיזה \"חדה\", נמוך = החלקה רכה.")]
    [Range(0.1f, 1f)] public float lateralStiffness = 0.5f;

    [Tooltip("כשהצמיג כבר מחליק, כמה מהחיכוח נשאר (0 עד 1). נמוך = ברגע שהרכב מתחיל להחליק קשה לעצור אותו.")]
    [Range(0.3f, 1f)] public float slidingFriction = 0.8f;

    [Tooltip("בלם יד: האחיזה של הגלגלים האחוריים (0 עד 1). נמוך = הזנב יוצא יותר.")]
    [Range(0f, 1f)] public float driftGripRear = 0.5f;

    [Tooltip("בלם יד: האחיזה של הגלגלים הקדמיים. גבוה = אפשר להמשיך לכוון.")]
    [Range(0f, 1f)] public float driftGripFront = 0.7f;

    [Tooltip("כמה מהר האחיזה נעלמת כשלוחצים על הרווח.")]
    public float gripLossSpeed = 12f;

    [Tooltip("כמה מהר האחיזה חוזרת כשעוזבים את הרווח. נמוך = ההחלקה נמשכת.")]
    public float gripRecovery = 4f;

    [Tooltip("המהירות (מטר בשנייה) שממנה פנייה חדה מתחילה לגרום להחלקה קלה.")]
    public float cornerSlideSpeed = 14f;

    [Tooltip("כמה אחיזה הולכת לאיבוד בפנייה חדה במהירות מלאה (0 עד 1).")]
    [Range(0f, 0.9f)] public float cornerSlideAmount = 0.35f;

    [Tooltip("הגנה מסחרור: כשהרכב מחליק בזווית גדולה מזו (מעלות), האחיזה מתחילה לחזור כדי שלא יסתובב סביב עצמו.")]
    public float catchAngleStart = 15f;

    [Tooltip("הזווית (מעלות) שבה האחיזה חוזרת במלואה. גבוה = אפשר להחליק בזווית גדולה יותר לפני שהרכב מתייצב.")]
    public float catchAngleEnd = 40f;

    [Tooltip("בלימת סיבוב בזמן שהרכב באוויר, כדי שלא יסתובב בלי שליטה אחרי קפיצה.")]
    public float airStability = 0.6f;
}

[Serializable]
public class CollisionSettings
{
    [Tooltip("כמה הרכב קופץ אחורה כשהוא פוגע בקיר (0 = נדבק, 1 = קופץ חזרה באותו כוח).")]
    [Range(0f, 1f)] public float bounciness = 0.35f;

    [Tooltip("מהירות פגיעה (מטר בשנייה) שמתחתיה זו נגיעה קלה בלי אפקטים מיוחדים.")]
    public float hardHitSpeed = 3.5f;

    [Tooltip("כמה מהירות הרכב מאבד בפגיעה חזיתית חזקה, בנוסף לקפיצה (0 עד 1). פגיעה צדדית מאבדת פחות.")]
    [Range(0f, 1f)] public float speedLossOnImpact = 0.25f;

    [Tooltip("כמה הרכב מסתובב אחרי פגיעה בזווית. 0 = בלי סיבוב.")]
    public float spinOnImpact = 0.35f;

    [Tooltip("הסיבוב הכי גדול שפגיעה יכולה לתת (רדיאנים בשנייה).")]
    public float maxImpactSpin = 5f;

    [Tooltip("עוצמת רעידת המצלמה בפגיעה חזקה. 0 = בלי רעידה.")]
    public float cameraShake = 0.25f;

    [Tooltip("אם מסומן, רכב שתקוע בקיר מקבל דחיפה קטנה החוצה.")]
    public bool unstick = true;

    [Tooltip("כמה שניות הרכב צריך להיות כמעט עומד ולדחוף קיר לפני שהוא מקבל דחיפה.")]
    public float unstickTime = 2f;

    [Tooltip("עוצמת הדחיפה (מטר בשנייה).")]
    public float unstickPush = 3.5f;
}

[Serializable]
public class PropSettings
{
    [Tooltip("חפץ שמשקלו קטן או שווה לזה (בק\"ג) נחשב קל: חרוט, פח, תיבת דואר.")]
    public float lightPropMass = 6f;

    [Tooltip("כשהרכב פוגע בחפץ קל, כמה מהמהירות שלו נשמרת (0 עד 1). גבוה = כמעט לא מאט.")]
    [Range(0f, 1f)] public float lightPropSpeedKeep = 0.8f;
}

[Serializable]
public class DebugSettings
{
    [Tooltip("מצייר בחלון Scene את קרני הקפיצים, הגלגלים ונקודות המגע (חלון Scene צריך להיות פתוח, ו-Gizmos דלוק).")]
    public bool drawDebug = false;
}
