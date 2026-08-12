
-- 2026-08-12T08:51:15.848Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T08:51:44.569Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T08:55:08.018Z
-- Seed san pham E2E-L4-SP01 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 1', N'E2E-L4-SP01',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-12T08:55:08.171Z
-- Seed san pham E2E-L4-SP02 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 2', N'E2E-L4-SP02',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-12T08:55:08.331Z
-- Seed san pham E2E-L4-SP03 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 3', N'E2E-L4-SP03',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-12T08:55:08.677Z
-- Seed nha cung cap SUP-01 cho L4-UJ-09

    INSERT INTO Suppliers (Id, Code, Name, ContactPerson, PhoneNumber, Email, Address, IsActive, CreatedAt)
    VALUES (NEWID(), N'SUP-01', N'E2E-L4 Nha cung cap kiem thu', N'Nguoi lien he',
            '0900000001', N'sup01@viettien.test', N'E2E-L4 Dia chi NCC', 1, SYSUTCDATETIME());

-- 2026-08-12T08:56:28.503Z
-- Seed nha cung cap SUP-01 cho L4-UJ-09

    INSERT INTO Suppliers (Id, Name, Code, ContactPerson, Phone, Email, Address, TaxCode, IsActive, CreatedAt)
    VALUES (NEWID(), N'E2E-L4 Nha cung cap kiem thu', N'SUP-01', N'Nguoi lien he',
            '0900000001', N'sup01@viettien.test', N'E2E-L4 Dia chi NCC',
            N'0100000000', 1, SYSUTCDATETIME());

-- 2026-08-12T08:56:45.965Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T08:57:01.694Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T08:58:29.650Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T08:58:45.956Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:18:00.410Z
-- L4-AM-07 dua don VT20260812091800319 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 2, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 4, PaymentStatus = 1
           WHERE Id = 'a417a264-38a6-4edb-94d6-aff8671c4b9d';

-- 2026-08-12T09:18:48.538Z
-- L4-AM-07 dua don VT20260812091848953 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = '51335bdf-ebf7-4982-814b-17e658bb7bc3';

-- 2026-08-12T09:21:13.828Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:21:31.015Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:22:05.387Z
-- L4-AM-07 dua don VT20260812092205267 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = 'b5544c0c-6f3e-413a-8f10-818465caf0ab';

-- 2026-08-12T09:25:33.907Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:25:50.579Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:26:23.903Z
-- L4-AM-07 dua don VT20260812092623635 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = '1157549c-b599-4633-825f-039ae6689c3a';

-- 2026-08-12T09:27:59.778Z
-- Seed L4-PM-07: chuyen don D49A6419-325C-4948-BC24-0F8A2EB85523 sang ho so khach khac de thu IDOR
UPDATE Orders SET CustomerProfileId = '701C3D1E-9C54-448C-ACCA-4DD9B493353D' WHERE Id = 'D49A6419-325C-4948-BC24-0F8A2EB85523';

-- 2026-08-12T09:30:31.264Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:30:48.009Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:31:21.532Z
-- L4-AM-07 dua don VT20260812093121343 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = 'f26cc7e9-7678-4a2d-a6d9-9aadc1b9eca1';

-- 2026-08-12T09:35:57.041Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:36:13.683Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:36:47.992Z
-- L4-AM-07 dua don VT20260812093647284 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = '7a9b99d1-a767-4f14-bfdf-54055014f47f';

-- 2026-08-12T09:43:11.524Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:43:28.516Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T09:44:02.442Z
-- L4-AM-07 dua don VT20260812094402563 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = '6e731422-e973-4973-bd1a-fa593fde5f84';

-- 2026-08-12T16:06:27.365Z
-- Seed san pham E2E-L4-SP01 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 1', N'E2E-L4-SP01',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-12T16:06:27.540Z
-- Seed san pham E2E-L4-SP02 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 2', N'E2E-L4-SP02',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-12T16:06:27.700Z
-- Seed san pham E2E-L4-SP03 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 3', N'E2E-L4-SP03',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-12T16:06:28.055Z
-- Seed nha cung cap SUP-01 cho L4-UJ-09

    INSERT INTO Suppliers (Id, Name, Code, ContactPerson, Phone, Email, Address, TaxCode, IsActive, CreatedAt)
    VALUES (NEWID(), N'E2E-L4 Nha cung cap kiem thu', N'SUP-01', N'Nguoi lien he',
            '0900000001', N'sup01@viettien.test', N'E2E-L4 Dia chi NCC',
            N'0100000000', 1, SYSUTCDATETIME());

-- 2026-08-12T16:08:53.274Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T16:09:08.439Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-12T16:09:39.280Z
-- L4-AM-07 dua don VT20260812160939383 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = 'c2842e00-cd4d-4ba0-beda-78323855afd4';
