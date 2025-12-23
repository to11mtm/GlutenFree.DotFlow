-- 📊 Lua Example - CSV Processing

local api = workflow.api

-- Read CSV file
local csvContent = api:ReadFileAsync("data/input.csv")
local data = api:ParseCsv(csvContent, true)

api:LogInfo(string.format("Read %d rows from CSV", #data))

-- Transform data
local transformed = {}
for i, row in ipairs(data) do
    if row.status == "active" then
        row.processed_date = api:FormatDateTime(api:Now(), "yyyy-MM-dd")
        table.insert(transformed, row)
    end
end

-- Write result
local outputCsv = api:ToCsv(transformed, true)
api:WriteFileAsync("data/output.csv", outputCsv)

api:LogInfo(string.format("✅ Wrote %d rows to output", #transformed))

return { output = transformed }

