
import yfinance as yf
import pandas_ta as ta

# أضف هنا أي عدد من الأسهم تريد فحصها الآن
stocks = ["1120.SR", "2222.SR", "2010.SR", "1150.SR", "AAPL", "NVDA", "NFLX", "MSFT", "TSLA"]

print(f"--- SNONA SMART RADAR: Scanning {len(stocks)} Stocks ---")

for s in stocks:
    try:
        # طلب بيانات آخر 6 أشهر لضمان وجود بيانات حتى والأسواق مغلقة
        df = yf.download(s, period="6mo", interval="1d", progress=False)
        
        # التأكد من أن البيانات ليست فارغة (بسبب إغلاق السوق)
        if df.empty or len(df) < 50:
            print(f"Stock: {s} | Status: Market Closed / No Data")
            continue

        # حساب المتوسطات EMA 20 و EMA 50
        ema20 = ta.ema(df['Close'], length=20).iloc[-1]
        ema50 = ta.ema(df['Close'], length=50).iloc[-1]
        last_price = df['Close'].iloc[-1]

        # منطق الاستراتيجية الخاصة بك
        status = "Strong Buy 🔥" if (last_price > ema20 and ema20 > ema50) else "Wait ⏳"
        
        print(f"Stock: {s:<8} | Price: {last_price:>8.2f} | Status: {status}")
        
    except Exception:
        print(f"Stock: {s:<8} | Status: Error")
