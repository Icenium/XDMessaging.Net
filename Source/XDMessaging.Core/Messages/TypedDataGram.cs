/*=============================================================================
*
*	(C) Copyright 2013, Michael Carlisle (mike.carlisle@thecodeking.co.uk)
*
*   http://www.TheCodeKing.co.uk
*  
*	All rights reserved.
*	The code and information is provided "as-is" without waranty of any kind,
*	either expressed or implied.
*
*=============================================================================
*/
using TheCodeKing.Utils.Contract;
using TheCodeKing.Utils.IoC;
using TheCodeKing.Utils.Serialization;
using XDMessaging.IoC;

namespace XDMessaging.Messages
{
    public class TypedDataGram<T> where T : class
    {
        #region Constants and Fields

        private readonly DataGram dataGram;
        private readonly ISerializer objectSerializer;

        #endregion

        #region Constructors and Destructors

        private TypedDataGram(DataGram dataGram, ISerializer serializer)
        {
            Validate.That(dataGram).IsNotNull();

            objectSerializer = serializer;
            this.dataGram = dataGram;
        }

        #endregion

        #region Properties

        public string AssemblyQualifiedName
        {
            get { return dataGram.AssemblyQualifiedName; }
        }

        public string Channel
        {
            get { return dataGram.Channel; }
        }

        public bool IsValid
        {
            get { return Message != null; }
        }

        public T Message
        {
            get { return objectSerializer.Deserialize<T>(dataGram.Message); }
        }

        #endregion

        #region Operators

        public static implicit operator DataGram(TypedDataGram<T> dataGram)
        {
            if (dataGram == null)
            {
                return null;
            }
            return dataGram.dataGram;
        }

        public static implicit operator TypedDataGram<T>(DataGram dataGram)
        {
            if (dataGram == null)
            {
                return null;
            }
            var serializer = SimpleIocContainerBootstrapper.GetInstance().Resolve<ISerializer>();
            return new TypedDataGram<T>(dataGram, serializer);
        }

        #endregion
    }
}