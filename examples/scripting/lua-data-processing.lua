-- 🌙 Lua Example - Simple Data Processing

-- Access the workflow API
local api = workflow.api

api:LogInfo("Starting Lua script execution! 🌙")

-- Get input data
local input = workflow.input

-- Process data
local result = {}
for i, item in ipairs(input) do
    if item.value > 100 then
        table.insert(result, {
            id = item.id,
            value = item.value * 2,
            processed = true
        })
    end
end

-- Set variable
api:SetVariable("filteredCount", #result)

-- Return output
return {
    output = result,
    count = #result
}

