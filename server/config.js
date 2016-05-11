const path = require('path');
const port = 5000;
const timeout = 60000;
const concurrency = 5;

module.exports = {
  common: {
    port: process.env.PORT || port,
    bucket_concurrency: concurrency,
    timeout: timeout,
    aws_key: process.env.AWS_KEY,
    aws_secret: process.env.AWS_SECRET,
    mongo_url: process.env.MONGO_URL || 'mongodb://127.0.0.1/imagesync'
  },
  development: {
    watch: path.resolve(__dirname, '../', 'images'),
  },
  production:  {
    bucket_concurrency: process.env.CONCURRENCY || concurrency,
    watch: process.env.PATH,
    timeout: Number(process.env.TIMEOUT || timeout)
  }
};