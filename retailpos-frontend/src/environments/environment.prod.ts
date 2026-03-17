/**
 * Production environment configuration
 * This file is used during production build (ng build --configuration production)
 */
export const environment = {
  production: true,
  apiUrl: 'https://api.yourproductiondomain.com/api', // Update with your production API URL
  apiTimeout: 30000, // 30 seconds
  enableDebugMode: false,
  version: '1.0.0'
};
