# Reach Image Sync

Watches a directory and syncs the files with S3.

#### Features

 - Folders in the watched directory are buckets
 - Recursively watches the directory
 - All files put into the bucket directories are sent to s3
 - Failed files will be retried