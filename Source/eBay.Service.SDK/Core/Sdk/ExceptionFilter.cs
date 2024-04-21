#region Copyright
/*
 * *
 *  * Copyright 2024 eBay Inc.
 *  *
 *  * Licensed under the Apache License, Version 2.0 (the "License");
 *  * you may not use this file except in compliance with the License.
 *  * You may obtain a copy of the License at
 *  *
 *  *  http://www.apache.org/licenses/LICENSE-2.0
 *  *
 *  * Unless required by applicable law or agreed to in writing, software
 *  * distributed under the License is distributed on an "AS IS" BASIS,
 *  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *  * See the License for the specific language governing permissions and
 *  * limitations under the License.
 *  *
 */
#endregion

#region Namespaces
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using eBay.Service.Core.Soap;

#endregion

namespace eBay.Service.Core.Sdk
{

	/// <summary>
	/// Exception filters are used to filter the exceptions that are to be logged
    /// using the <see cref="ApiLogManager"/> object, which is set in the
    /// <see cref="ApiContext"/> for the application. Only those exceptions specified
    /// in this filter will be logged. If the filter is not used, then all exceptions
    /// are logged.
	/// </summary>
	[ClassInterface(ClassInterfaceType.AutoDispatch)]
	public class ExceptionFilter
	{

		#region Constructors
		/// <summary>
		/// 
		/// </summary>
		public ExceptionFilter() 
		{
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="errorCodes"></param>
		/// <param name="exceptions"></param>
		/// <param name="httpStatusCodes"></param>
		public ExceptionFilter(string errorCodes, string exceptions, string httpStatusCodes)
		{
			mTriggerErrorCodes = ParseTriggerErrorCodes(errorCodes);
			mTriggerHttpStatusCodes = ParseTriggerHttpStatusCodes(httpStatusCodes);
			mTriggerExceptions = ParseTriggerExceptions(exceptions);
		}
		#endregion

		#region Public Methods
		/// <summary>
		/// Determines if an exception matches the criteria that have been set up for matching.
        /// 1. If no exceptions or codes have been configured to match, then the exception does not match the filter.  (Empty filters do not
        ///    match any exception.)
        /// 2. If the exception is an <see cref="eBay.Service.Core.Sdk.ApiException"/> and has an error code configured in the CallRetry
        ///    <see cref="eBay.Service.Core.Sdk.CallRetry.TriggerErrorCodes"/>, the exception matches. 
        ///    (See <see href="http://www.developer.ebay.com/DevZone/XML/docs/Reference/eBay/Errors/ErrorMessages.htm">
        ///     Errors by Number</see> in the Trading API Call Reference for a list of possible errors.)
        /// 3. If the exception is an ApiException containing (wrapping) an <see cref="eBay.Service.Core.Sdk.HttpException"/> 
        ///    with a code configured in <see cref="eBay.Service.Core.Sdk.CallRetry.TriggerHttpStatusCodes"/>,
        ///    the exception matches.
		/// 4. If the exception is one of the types (classes) configured in TriggerExceptions, the exception matches.  HttpException is 
		///    supported for matching even though it arrives wrapped in an ApiException, because we unwrap the outer exception and match 
        ///    on the inner HttpException type.
        /// 5. Otherwise, the exception does not match the filter.        
		/// </summary>
		/// <param name="ex">The <see cref="System.Exception"/> to test for retry.</param>
		/// <returns>Returns <b>true</b> if retry should be invoked, else <b>false</b></returns>
		public bool Matches(Exception ex)
		{
			if (ex == null)
				return false;

			if ((mTriggerErrorCodes == null || mTriggerErrorCodes.Length == 0) && (mTriggerExceptions == null || mTriggerExceptions.Length == 0)
				&& (mTriggerHttpStatusCodes == null || mTriggerHttpStatusCodes.Length == 0))
			{
				return false;
			}

			if (mTriggerErrorCodes != null && mTriggerErrorCodes.Length > 0 && ex.GetType() == typeof(ApiException))
			{
				ApiException apiex = (ApiException) ex;
				IEnumerator itenum = apiex.Errors.GetEnumerator();
				while (itenum.MoveNext())
				{
					if (((ErrorType)itenum.Current).SeverityCode == SeverityCodeType.Error && InTriggerErrorCodes((ErrorType)itenum.Current))
						return true;
				}
			}
			
			// If the exception wraps an HttpException, unwrap it and work with that exception directly for retry purposes (only).
			if (ex.GetType() == typeof(ApiException) && ex.InnerException != null && ex.InnerException.GetType() == typeof(HttpException))
				ex = ex.InnerException;

			if (mTriggerHttpStatusCodes != null && mTriggerHttpStatusCodes.Length > 0 && ex.GetType() == typeof(HttpException))
			{
				HttpException httpex = (HttpException) ex;
				int code = httpex.StatusCode;
				if (InTriggerHttpStatusCodes(code))
					return true;
			}
			if (mTriggerExceptions != null && mTriggerExceptions.Length > 0)
				if (InTriggerExceptions(ex))
					return true;

			return false;
		}
		/// <summary>
		/// Converts comma-separated list of error codes into the appropriate String[] ready to set into the filter.
		/// </summary>
		/// <param name="configString"></param>
		/// <returns></returns>
		public static string[] ParseTriggerErrorCodes(string configString)
		{
			if (configString == null || configString.Length == 0)
				return null;
			string[] tokens = configString.Split(SEPS);
			return tokens;
		}
		/// <summary>
		/// Converts comma-separated list of integer status codes into the appropriate Int32[] ready to set into the filter.
		/// </summary>
		/// <param name="configString"></param>
		/// <returns></returns>
		public static int[] ParseTriggerHttpStatusCodes(string configString)
		{
			if (configString == null || configString.Length == 0)
				return null;
			
            string[] tokens = configString.Split(SEPS);

            return tokens.Select(int.Parse).ToArray();
        }
		/// <summary>
		/// Converts comma-separated list of exception type names into the appropriate Type[] ready to set into the filter.
		/// </summary>
		/// <param name="configString"></param>
		/// <returns></returns>
		public static Type[] ParseTriggerExceptions(string configString)
		{
			if (configString == null || configString.Length == 0)
				return null;
		    var outCollection = new List<Type>();
			string[] tokens = configString.Split(SEPS);
			foreach (var token in tokens)
            {
                string typename = token.ToLower(System.Globalization.CultureInfo.InvariantCulture);
                Type t;
                switch (typename)
                {
                    // Normally you must fully-qualify the name, e.g. eBay.Service.Core.Sdk.ApiException.
                    // We allow shorthand for the 3 obvious ones.
                    case "apiexception":
                        t = typeof(ApiException);
                        break;
                    case "httpexception":
                        t = typeof(HttpException);
                        break;
                    case "sdkexception":
                        t = typeof(SdkException);
                        break;
                    default:
                        t = Type.GetType(token);
                        break;
                }
                outCollection.Add(t);
            }
			return outCollection.ToArray();
		}
		#endregion

		#region Private Methods
		private bool InTriggerErrorCodes(ErrorType error)
		{
			IEnumerator tecenum = mTriggerErrorCodes.GetEnumerator();
			while (tecenum.MoveNext())
			{
				if( error.ErrorCode == (string) tecenum.Current )
					return true;
			}
			return false;
		}

		private bool InTriggerHttpStatusCodes(int code)
		{
			IEnumerator thscenum = mTriggerHttpStatusCodes.GetEnumerator();
			while (thscenum.MoveNext())
			{
				if( code == (Int32) thscenum.Current )
					return true;
			}
			return false;
		}
		private bool InTriggerExceptions(Exception ex)
		{
			IEnumerator teenum = mTriggerExceptions.GetEnumerator();
			while (teenum.MoveNext())
			{
				if( ex.GetType() == (System.Type) teenum.Current)
					return true;
				else if (ex.InnerException != null)
					return (InTriggerExceptions(ex.InnerException));
			}
			return false;
		}
		#endregion

		#region Properties
		/// <summary>
		/// Gets or sets the error codes that retry should occur for of type <see cref="String[]"/>.
		/// </summary>
		public string[] TriggerErrorCodes
		{
			get { return mTriggerErrorCodes; }
			set { mTriggerErrorCodes = value ; }
		}

		/// <summary>
		/// Gets or sets the error codes that retry should occur for of type <see cref="String[]"/>.
		/// </summary>
		public int[] TriggerHttpStatusCodes
		{
			get { return mTriggerHttpStatusCodes; }
			set { mTriggerHttpStatusCodes = value ; }
		}

		/// <summary>
		/// Gets or sets the exception types that retry should occur for of type <see cref="Type[]"/>.
		/// </summary>
		public Type[] TriggerExceptions
		{
			get { return mTriggerExceptions; }
			set { mTriggerExceptions = value ; }
		}
		#endregion

		#region Private Fields
		private string[] mTriggerErrorCodes = Array.Empty<string>();
		private int[] mTriggerHttpStatusCodes = Array.Empty<int>();
		private Type[] mTriggerExceptions = new Type[0];
		private static char [] SEPS = {';'};
		#endregion

	}
}
