
-- 2026-08-14T02:45:41.995Z
-- Seed san pham E2E-L4-SP01 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 1', N'E2E-L4-SP01',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-14T02:45:42.206Z
-- Seed san pham E2E-L4-SP02 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 2', N'E2E-L4-SP02',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-14T02:45:42.383Z
-- Seed san pham E2E-L4-SP03 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 3', N'E2E-L4-SP03',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-14T02:45:42.733Z
-- Seed nha cung cap SUP-01 cho L4-UJ-09

    INSERT INTO Suppliers (Id, Name, Code, ContactPerson, Phone, Email, Address, TaxCode, IsActive, CreatedAt)
    VALUES (NEWID(), N'E2E-L4 Nha cung cap kiem thu', N'SUP-01', N'Nguoi lien he',
            '0900000001', N'sup01@viettien.test', N'E2E-L4 Dia chi NCC',
            N'0100000000', 1, SYSUTCDATETIME());

-- 2026-08-14T02:48:04.143Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-14T02:48:19.525Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-14T02:48:37.313Z
-- L4-AM-07 dua don VT20260814024837488 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = '7b7b7499-b726-467d-991a-b9a0e3b21d7b';

-- 2026-08-14T11:57:09.630Z
-- Seed san pham E2E-L4-SP01 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 1', N'E2E-L4-SP01',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-14T11:57:09.802Z
-- Seed san pham E2E-L4-SP02 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 2', N'E2E-L4-SP02',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-14T11:57:09.956Z
-- Seed san pham E2E-L4-SP03 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 3', N'E2E-L4-SP03',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-14T11:57:10.311Z
-- Seed nha cung cap SUP-01 cho L4-UJ-09

    INSERT INTO Suppliers (Id, Name, Code, ContactPerson, Phone, Email, Address, TaxCode, IsActive, CreatedAt)
    VALUES (NEWID(), N'E2E-L4 Nha cung cap kiem thu', N'SUP-01', N'Nguoi lien he',
            '0900000001', N'sup01@viettien.test', N'E2E-L4 Dia chi NCC',
            N'0100000000', 1, SYSUTCDATETIME());

-- 2026-08-14T12:54:07.775Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-14T12:54:24.218Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-14T12:54:41.301Z
-- L4-AM-07 dua don VT20260814125441837 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = 'e14c43cf-3cbd-46f9-8319-e90129f4e5d1';
