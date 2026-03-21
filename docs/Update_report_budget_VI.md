# Hệ Thống Quản Lý Ngân Sách Ảo và Dòng Tiền
## Kế Hoạch Nâng Cấp Cho Chế Độ Giao Dịch Ảo (Paper Trading)

---

## Tóm Tắt Điều Hành

Hệ thống báo cáo giao dịch ảo hiện tại **THIẾU** khả năng theo dõi dòng tiền và ngân sách. Các báo cáo chỉ hiển thị số lượng giao dịch và lợi nhuận ghi nhận, nhưng **KHÔNG theo dõi**:
- Vốn ban đầu được cấp phát ($500, $10,000, v.v.)
- Thay đổi số dư tiền mặt qua các phiên giao dịch
- Lợi nhuận/lỗ dưới dạng phần trăm so với ngân sách
- Các giao dịch nạp/rút tiền ảo
- Đường cong vốn độc lập (giá trị tài khoản theo thời gian)

Tài liệu này cung cấp giải pháp hoàn chỉnh để thêm quản lý ngân sách và theo dõi dòng tiền vào hệ thống.

---

## 1) Vấn Đề Hiện Tại

### Những Hạn Chế

1. **Không Theo Dõi Vốn**: Không ghi lại vốn khởi tạo (ví dụ: $500) hay số dư hiện tại
2. **Không Lịch Sử Ngân Sách**: Không thể xem thay đổi dòng tiền qua các phiên giao dịch
3. **Tầm Nhìn Hạn Chế**: Báo cáo hiển thị giao dịch nhưng không kết nối đến tác động vốn
4. **Không Quản Lý Vốn**: Không thể điều chỉnh vốn ảo cho các lần kiểm thử tiếp theo
5. **Các Chỉ Số Không Đầy Đủ**: Thiếu các chỉ số chính:
   - Số dư tiền mặt khả dụng hiện tại
   - Tổng vốn (tiền mặt + giá trị các vị thế mở)
   - Lợi nhuận trên vốn (ROI) %
   - Rút vốn tối đa từ vốn khởi tạo
   - Thay đổi vốn theo từng phiên

### Tác Động Đến Kiểm Thử

- Người kiểm thử không thể xác minh hệ thống có lợi nhuận/bền vững không
- Không thể mô phỏng ràng buộc vốn (ví dụ: "chạy với tiền hạn chế để kiểm tra quản lý ký quỹ")
- Khó tương quan giữa quyết định giao dịch và tác động vốn
- Khó thực hiện phân tích "what-if" với các vốn khởi tạo khác nhau

---

## 2) Giải Pháp Đề Xuất

### Khái Niệm Chính: Sổ Cái Vốn Giao Dịch

Theo dõi vốn ảo thông qua một **hệ thống dựa trên sổ cái** ghi lại:
- Số dư mở phiên trên mỗi phiên
- Lợi nhuận ghi nhận trên mỗi phiên (từ các giao dịch đã đóng)
- Điều chỉnh vốn (nạp/rút cho mục đích kiểm thử)
- Số dư đóng phiên trên mỗi phiên
- Đường cong vốn (tổng giá trị tài khoản = tiền mặt + giá trị thị trường các vị thế mở)

### Các Tính Năng Chính

#### A. Quản Lý Ngân Sách
- Đặt vốn ảo khởi tạo (mặc định: $10,000 USDT cho chế độ ảo)
- Vốn riêng biệt cho giao dịch ảo vs giao dịch thực
- Hỗ trợ nhiều kịch bản kiểm thử bằng cách đặt lại vốn

#### B. Theo Dõi Dòng Tiền
- Theo dõi thay đổi số dư tiền mặt trên mỗi phiên
- Hiển thị tổng vốn chạy qua thời gian
- Hiển thị lợi nhuận chưa ghi nhận vs ghi nhận riêng biệt

#### C. Hoạt Động Vốn
- **Nạp Tiền**: Thêm vốn ảo (ví dụ: "thêm $500 cho giai đoạn kiểm thử tiếp theo")
- **Rút Tiền**: Xóa vốn (ví dụ: "trích lợi nhuận để đặt lại cho kiểm thử sạch")
- **Đặt Lại**: Xóa tất cả lịch sử và bắt đầu với ngân sách mới
- **Ảnh Chụp**: Ghi lại trạng thái vốn tại một thời điểm để so sánh

#### D. Chỉ Số & KPI
```
Chỉ Số Vốn:
- Vốn Ban Đầu: Số tiền khởi đầu
- Vốn Hiện Tại: Tổng giá trị tài khoản
- Tiền Mặt Khả Dụng: Vốn chưa được cấp phát
- Lợi Nhuận Ghi Nhận: Lợi nhuận/lỗ từ các giao dịch đã đóng
- Lợi Nhuận Chưa Ghi Nhận: Lợi nhuận/lỗ từ các vị thế mở
- Tổng Lợi Nhuận: Ghi nhận + Chưa ghi nhận
- ROI %: Tổng Lợi Nhuận / Vốn Ban Đầu * 100
- Rút Vốn Tối Đa %: (Vốn Mạnh Nhất - Vốn Yếu Nhất) / Vốn Mạnh Nhất * 100
- Thay Đổi Vốn %: (Vốn Hiện Tại - Vốn Ban Đầu) / Vốn Ban Đầu * 100

Chỉ Số Phiên:
- Số Dư Mở Phiên
- Số Dư Đóng Phiên
- Lợi Nhuận Phiên
- Thay Đổi Vốn Phiên %
```

---

## 3) Bảng Cơ Sở Dữ Liệu Cần Thêm

### Bảng Mới 1: `paper_trading_ledger`
Theo dõi tất cả các giao dịch vốn và số dư cho giao dịch ảo.

| Trường | Kiểu | Mô Tả |
|--------|------|-------|
| id | UUID | Khóa chính |
| recorded_at_utc | TIMESTAMPTZ | Thời gian ghi lại |
| reference_type | VARCHAR | 'INITIAL', 'SESSION_PNL', 'DEPOSIT', 'WITHDRAW', 'RESET' |
| reference_id | TEXT | session_id, transaction_id, v.v. |
| cash_balance_before | NUMERIC | Số dư trước giao dịch này |
| cash_balance_after | NUMERIC | Số dư sau giao dịch này |
| adjustment_amount | NUMERIC | Số tiền thêm/bớt (dương=vào, âm=ra) |
| description | TEXT | Mô tả chi tiết |
| created_by | VARCHAR | 'System', 'AUTOMATED/session_id', 'USER/username' |

### Bảng Mới 2: `session_capital_snapshot`
Ảnh chụp trạng thái vốn ở ranh giới phiên.

| Trường | Kiểu | Mô Tả |
|--------|------|-------|
| session_id | VARCHAR | Ví dụ: '20260322-S1' |
| session_date | DATE | Ngày phiên |
| opening_cash_balance | NUMERIC | Tiền mặt lúc mở phiên |
| closing_cash_balance | NUMERIC | Tiền mặt lúc đóng phiên |
| session_realized_pnl | NUMERIC | Lợi nhuận ghi nhận trong phiên |
| closing_equity_total | NUMERIC | Tổng vốn lúc đóng phiên |
| closing_holdings_value | NUMERIC | Giá trị tài sản giữ lúc đóng phiên |
| session_equity_change_pct | NUMERIC | % thay đổi vốn phiên |

---

## 4) Các API Endpoint Cần Thêm

### 4.1) Quản Lý Ngân Sách

#### GET `/api/trading/budget/status`
Trả về trạng thái ngân sách hiện tại cho giao dịch ảo.

```json
{
  "initialCapital": 10000.00,
  "currentCashBalance": 9523.45,
  "totalHoldingsValue": 150.22,
  "totalEquity": 9673.67,
  "totalRealizedPnL": -326.33,
  "totalUnrealizedPnL": -0.00,
  "totalPnL": -326.33,
  "roiPercent": -3.26
}
```

#### POST `/api/trading/budget/deposit`
Thêm vốn ảo.

```json
Yêu cầu: {
  "amount": 500.00,
  "description": "Vốn kiểm thử bổ sung",
  "requestedBy": "tester@example.com"
}

Phản hồi: {
  "success": true,
  "newBalance": 10173.67
}
```

#### POST `/api/trading/budget/withdraw`
Rút vốn ảo.

#### POST `/api/trading/budget/reset`
Đặt lại vốn ban đầu, xóa tất cả giao dịch.

#### GET `/api/trading/budget/ledger`
Lấy lịch sử giao dịch vốn.

#### GET `/api/trading/budget/equity-curve`
Lấy giá trị vốn theo thời gian để vẽ biểu đồ.

---

## 5) Cải Tiến Giao Diện Frontend

### 5.1) Widget Ngân Sách Mới: Thẻ Tổng Quan Ngân Sách

Hiển thị trên trang giao dịch chính:

```
╔════════════════════════════════════════════════╗
║      NGÂN SÁCH GIAO DỊCH ẢO                   ║
├════════════════════════════════════════════════┤
│                                                │
│  Vốn Ban Đầu:           $10,000 USDT         │
│  Số Dư Hiện Tại:         $9,850 USDT         │  
│  Tổng Vốn:               $9,975.50 USDT      │
│                                                │
│  Tổng Lợi Nhuận:        -$24.50  (-0.25%)   │
│    ├─ Ghi Nhận:         +$125.50             │
│    └─ Chưa Ghi Nhận:     -$150.00            │
│                                                │
│  Rút Vốn Tối Đa:         -5.12%              │
│  ROI Hiện Tại:           -0.25%              │
│                                                │
│  [Nạp +] [Rút -] [Đặt Lại] [Lịch Sử]        │
│                                                │
└════════════════════════════════════════════════┘
```

### 5.2) Tab "Dòng Tiền" Trong Trang Báo Cáo Phiên

Phần mới hiển thị:
- Số dư mở/đóng trên mỗi phiên
- Biểu đồ đường cong vốn
- Biểu đồ tròn phân bổ vốn
- Bảng sổ cái (nạp, rút, lợi nhuận phiên)

### 5.3) Modal Quản Lý Ngân Sách

Hộp thoại bật lên với biểu mẫu cho:
- **Nạp Tiền**: Nhập số tiền, mô tả, gửi
- **Rút Tiền**: Nhập số tiền, xác nhận
- **Đặt Lại**: Xác nhận đặt lại, đặt vốn ban đầu mới
- **Lịch Sử**: Xem tất cả giao dịch

---

## 6) Lộ Trình Triển Khai

| Giai Đoạn | Thời Gian | Mục Tiêu |
|-----------|----------|---------|
| 1 | 1-2 tuần | Tạo bảng cơ sở dữ liệu, kịch bản di chuyển |
| 2 | 1-2 tuần | Tạo API backend quản lý ngân sách |
| 3 | 1 tuần | Tích hợp phiên giao dịch tự động |
| 4 | 2 tuần | Xây dựng UI giao diện người dùng |
| 5 | 1 tuần | Kiểm thử E2E, tối ưu hóa, tài liệu |

---

## 7) Ví Dụ Quy Trình: Kịch Bản Kiểm Thử Hoàn Chỉnh

### Ngày 1: Thiết Lập Ban Đầu
- Hệ thống khởi tạo với $10,000 USDT ngân sách giao dịch ảo
- Sổ cái ghi: `INITIAL | trước: $0 | sau: $10,000`

### Ngày 1 Các Phiên
- **Phiên S1 (00:00-04:00)**: Giao dịch $500, lợi nhuận +$50 → sổ cái ghi `SESSION_PNL`
- **Phiên S2 (04:00-08:00)**: Lỗ -$25 → sổ cái ghi `SESSION_PNL`
- Đường cong vốn biểu diễn: $10,000 → $10,025 (net +$25)

### Ngày 2: Điều Chỉnh Giữa Kiểm Thử
- Người kiểm thử quyết định kiểm thử với vốn nhiều hơn
- Gọi `POST /api/trading/budget/deposit` với $500
- Sổ cái ghi: `DEPOSIT | trước: $10,025 | sau: $10,525`
- Báo cáo hiển thị đường cơ sở mới cho tính ROI

### Ngày 3: Hoàn Thành Kiểm Thử
- Sau 6 phiên nữa, tổng lợi nhuận là +$175
- Vốn cuối cùng: $10,700
- Người kiểm thử gọi `POST /api/trading/budget/reset` với vốn=$5,000
- Sổ cái **MỚI** được tạo cho kiểm thử Giai Đoạn 2
- Sổ cái trước được lưu trữ để phân tích

---

## 8) Tiêu Chí Thành Công

### Chỉ Số Chính
- ✅ Số dư ngân sách hiển thị và cập nhật theo thời gian thực
- ✅ Có thể nạp/rút vốn ảo
- ✅ Ảnh chụp vốn phiên được điều hòa cùng lợi nhuận order
- ✅ Biểu đồ đường cong vốn hiển thị chính xác tiến trình
- ✅ Tất cả kịch bản kiểm thử vượt qua

### Hiệu Suất
- ✅ Endpoint ngân sách phản hồi < 100ms
- ✅ Truy vấn sổ cái 10k dòng hoàn thành < 500ms
- ✅ Tính đường cong vốn 30 ngày < 1s
- ✅ API xử lý 1000 req/s tải đồng thời

### Trải Nghiệm Người Dùng
- ✅ Thẻ ngân sách hiển thị trên trang chủ
- ✅ Quy trình nạp/rút < 3 nhấp chuột
- ✅ Dữ liệu lịch sử có khả năng tìm kiếm và xuất
- ✅ Thông báo lỗi rõ ràng cho hoạt động không hợp lệ

---

## Kết Luận

Giải pháp này chuyển đổi hệ thống báo cáo từ quan điểm **dựa trên giao dịch** sang quan điểm **dựa trên vốn**. Những người kiểm thử và nhà giao dịch hiện có thể:

1. ✅ Xem dòng tiền thực và thay đổi vốn theo thời gian thực
2. ✅ Quản lý vốn ảo cho các kịch bản kiểm thử khác nhau
3. ✅ Tương quan giữa quyết định giao dịch và tác động tài chính
4. ✅ Có sự tự tin rằng hệ thống có khả năng theo dõi lợi nhuận
5. ✅ Xác minh hành vi hệ thống dưới các ràng buộc vốn khác nhau

Triển khai theo các thực hành tốt nhất cho các hệ thống sổ cái tài chính với theo dõi kiểm toán đầy đủ, kiểm tra hòa giải và kiểm soát truy cập dựa trên quyền.
