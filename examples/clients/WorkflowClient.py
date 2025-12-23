# 🐍 Python client SDK

import requests
from typing import Dict, Any, Optional
import time

class WorkflowClient:
    def __init__(self, base_url: str, api_key: Optional[str] = None):
        self.base_url = base_url
        self.api_key = api_key
        self.session = requests.Session()
        if api_key:
            self.session.headers['X-API-Key'] = api_key
    
    def execute_workflow(
        self, 
        workflow_name: str, 
        inputs: Optional[Dict[str, Any]] = None
    ) -> Dict[str, Any]:
        """Execute a workflow ✨"""
        response = self.session.post(
            f"{self.base_url}/api/v1/workflows/execute/{workflow_name}",
            json={"inputs": inputs or {}}
        )
        response.raise_for_status()
        return response.json()
    
    def execute_workflow_sync(
        self,
        workflow_name: str,
        inputs: Optional[Dict[str, Any]] = None,
        timeout_seconds: int = 300
    ) -> Dict[str, Any]:
        """Execute and wait for completion ⏳"""
        response = self.session.post(
            f"{self.base_url}/api/v1/workflows/execute/{workflow_name}",
            json={
                "inputs": inputs or {},
                "waitForCompletion": True,
                "timeoutSeconds": timeout_seconds
            }
        )
        response.raise_for_status()
        return response.json()
    
    def get_execution_status(self, execution_id: str) -> Dict[str, Any]:
        """Get execution status 📊"""
        response = self.session.get(
            f"{self.base_url}/api/v1/workflows/executions/{execution_id}"
        )
        response.raise_for_status()
        return response.json()
    
    def wait_for_completion(
        self,
        execution_id: str,
        poll_interval: float = 2.0,
        timeout: Optional[float] = None
    ) -> Dict[str, Any]:
        """Wait for execution to complete 🎯"""
        start_time = time.time()
        
        while True:
            status = self.get_execution_status(execution_id)
            
            if status['status'] in ['Completed', 'Failed', 'Cancelled']:
                return status
            
            if timeout and (time.time() - start_time) > timeout:
                raise TimeoutError(f"Execution timed out after {timeout} seconds")
            
            time.sleep(poll_interval)

# Usage:
# client = WorkflowClient('https://workflow-engine.example.com', api_key='your-api-key')
# 
# # Execute workflow
# execution = client.execute_workflow('data-sync', {
#     'source': 'api',
#     'destination': 'database'
# })
# 
# print(f"Execution started: {execution['executionId']}")
# 
# # Wait for completion
# result = client.wait_for_completion(execution['executionId'])
# print(f"Status: {result['status']}, Duration: {result['duration']}")

