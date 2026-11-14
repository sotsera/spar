Server 17.00.1155
## nvarchar with lone surrogates
- SELECT N'a' + NCHAR(0xD800) + N'b': units [0061 FFFD 0062], stored bytes 0x610000D86200, server collation SQL_Latin1_General_CP1_CI_AS
- SELECT @p with @p = "a\uDC00b" (nvarchar): units [0061 FFFD 0062], DATALENGTH 6, bytes 0x6100FDFF6200
## decimal and money
- decimal(38,0) 38 nines: GetDecimal THROWS OverflowException: Conversion overflows.
  GetSqlDecimal 99999999999999999999999999999999999999 (String); GetValue THROWS OverflowException: Conversion overflows.
- decimal(10,4) 1.5: 1.5000 (Decimal); money max: 922337203685477.5807 (Decimal); smallmoney min: -214748.3648 (Decimal)
## dates and times
- datetime2(7): 2026-11-14T10:20:30.1234567 (String), Kind Unspecified
- datetime: 2026-11-14T10:20:30.9970000 (String)
- datetimeoffset(7): 2026-11-14T10:20:30.1234567+01:00 (String)
- date: GetValue 11/14/2026 00:00:00 (DateTime); GetFieldValue<DateOnly> 11/14/2026 (DateOnly)
- time(7): GetValue 10:20:30.1234567 (TimeSpan); GetFieldValue<TimeOnly> 10:20:30.1234567 (String)
- SYSUTCDATETIME(): Kind Unspecified
## uniqueidentifier, rowversion, float
- uniqueidentifier: GetGuid 3f2504e0-4f89-11d3-9a0c-0305e82c3301; CAST AS varchar 3F2504E0-4F89-11D3-9A0C-0305E82C3301; @@DBTS 0x0000000000000FA0 (byte[])
- float overflow: THROWS SqlException: Arithmetic overflow error converting expression to data type float.
- float NaN parameter: THROWS SqlException: The incoming tabular data stream (TDS) remote procedure call (RPC) protocol stream is incorrect. Parameter 3 ("@f"): The supplied value is not a valid instance of data type float. Check the source data for invalid values. An example of an invalid value is data of numeric type with scale greater than precision.
## bigint values a monitor reads
- sys.dm_os_sys_info.cpu_ticks = 546893217367201 (below 2^53-1; 6,07% of it); ms_ticks = 182569309; uptime 182432 s
- largest CAST(query_hash AS bigint) in sys.dm_exec_query_stats: -4944027996587653929
