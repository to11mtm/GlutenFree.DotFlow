# 🐍 Python Example - Data Analysis

# Access the workflow API
api = workflow.api

api.LogInfo("Starting Python script execution! 🐍")

# Get input data
input_data = workflow.input

# Process with list comprehension
filtered = [
    item for item in input_data 
    if item['score'] > 75
]

# Calculate statistics
total = len(filtered)
avg_score = sum(item['score'] for item in filtered) / total if total > 0 else 0

api.LogInfo(f"Processed {total} items with average score {avg_score:.2f}")

# Set workflow variables
api.SetVariable("total_processed", total)
api.SetVariable("average_score", avg_score)

# Return result
return {
    'output': filtered,
    'stats': {
        'total': total,
        'average': avg_score
    }
}

