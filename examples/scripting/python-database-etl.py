# 🗄️ Python Example - Database ETL

import json

api = workflow.api

# Get connection string from variable
conn_string = api.GetVariable("dbConnection")

# Extract data from source
api.LogInfo("📥 Extracting data from database...")
source_data = await api.QueryDatabaseAsync(
    conn_string,
    "SELECT * FROM source_table WHERE status = @status",
    {"status": "pending"}
)

api.LogInfo(f"Found {len(source_data)} records to process")

# Transform data
transformed = []
for row in source_data:
    transformed.append({
        'id': row['id'],
        'name': row['name'].upper(),
        'processed_date': api.FormatDateTime(api.Now(), "yyyy-MM-dd HH:mm:ss"),
        'metadata': json.dumps(row.get('metadata', {}))
    })

# Load into destination
api.LogInfo("📤 Loading data into destination...")
for item in transformed:
    await api.ExecuteDatabaseAsync(
        conn_string,
        """INSERT INTO destination_table (id, name, processed_date, metadata) 
           VALUES (@id, @name, @processed_date, @metadata)""",
        item
    )

api.LogInfo(f"✅ Successfully processed {len(transformed)} records")

return {
    'output': transformed,
    'count': len(transformed)
}

