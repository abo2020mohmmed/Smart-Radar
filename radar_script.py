import yfinance as yf
import pandas_ta as ta

# قائمة الأسهم المختارة (يمكنك زيادة القائمة كما تشاء)
stocks = ["1120.SR", "2222.SR", "AAPL", "NVDA", "NFLX", "MSFT"]

print("--- SNONA SMART RADAR: RUNNING ---")

for s in stocks:
    try:
        # جلب بيانات كافية للحسابات
        df = yf.download(s, period="1mo", interval="1d", progress=False)
        
        # التأكد من أن البيانات ليست فارغة وموجودة بشكل صحيح
        if df is None or df.empty or len(df) < 2:
            print(f"Stock: {s:<8} | Status: Skip (No Data)")
            continue
            
        # معالجة البيانات لضمان عدم وجود أخطاء في التنسيق
        close_prices = df['Close'].dropna()
        
        # حساب المؤشرات EMA 20 و 50
        ema20 = ta.ema(close_prices, length=20)
        ema50 = ta.ema(close_prices, length=50)

        # استخراج آخر القيم
        current_price = float(close_prices.iloc[-1])
        
        # في حال كانت البيانات أقل من المطلوب للمؤشرات، نعطي حالة انتظار
        if ema20 is None or ema50 is None:
            print(f"Stock: {s:<8} | Status: Calculating...")
            continue
            
        last_ema20 = float(ema20.iloc[-1])
        last_ema50 = float(ema50.iloc[-1])

        # منطق تحديد الحالة بناءً على خوارزميتك
        status = "Strong Buy 🔥" if (current_price > last_ema20 and last_ema20 > last_ema50) else "Wait ⏳"
        
        print(f"Stock: {s:<8} | Price: {current_price:>8.2f} | Status: {status}")
        
    except Exception as e:
        print(f"Stock: {s:<8} | Status: Processing...")

print("--- SCAN COMPLETED ---")
