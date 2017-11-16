using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace NeoFund
{
    public class NeoFundContract : Neo.SmartContract.Framework.SmartContract
    {
        private static readonly byte[] admin = { 0 };
        //public static TransactionOutput[] references = new TransactionOutput[0];

        public static Object Main(string operation, params object[] args)
        {
            Runtime.Notify("Version 1.0");
            Runtime.Notify("Runtime.Trigger", Runtime.Trigger);
            Runtime.Notify("operation", operation);
            Runtime.Notify("arg length ", args.Length);
            Runtime.Notify("args", args);

            // Contract transaction, ie assest deposit/withdrawl transaction (operation == signature)
            if (Runtime.Trigger == TriggerType.Verification)
            {
                TransactionOutput senderObject = GetSenderObject(GetSenderObjects());
                byte[] contributorSH = senderObject.ScriptHash;
                Runtime.Notify("TriggerType.Verification => contributorSH", contributorSH);

                if (IsContributorSH(contributorSH))
                {
                    // Getting Requested TX value
                    BigInteger withdrawRequested = senderObject.Value / 100000000;

                    // Getting pre approved withdraw value for contributorSH
                    BigInteger withdrawHold = StorageGet(contributorSH.AsString(), "withdrawHold").AsBigInteger();

                    // This should always be 0
                    BigInteger balance = withdrawRequested - withdrawHold;

                    if (balance == 0)
                    {
                        Runtime.Notify("TriggerType.Verification => Passed transaction");
                        return true;
                    }

                    Runtime.Notify("TriggerType.Verification => Withdraw value needs to be pre approved via WithdrawFundsRequest");
                }
            }

            // Invocation transaction
            else if (Runtime.Trigger == TriggerType.Application)
            {
                Runtime.Notify("TriggerType.Application");
                // Operation Permissions:
                //      Admin:          SetFee
                //      Creator:        CreateFund, DeleteFund
                //      Contributor:    DepositFunds, GetFundParameter, ReachedGoal, ReachedEndTime, IsRefundActive, GetContributorInfo, CheckContributorOwed 

                // ADMIN // 
                // TODO - Does nothing
                // SET FEE 
                if (operation == "SetFee") return SetFee((byte[])args[0], (BigInteger)args[1]);

                // CREATOR //
                // CREATE FUND
                if (operation == "CreateFund")
                {
                    Runtime.Notify("Creating New Fund");
                    
                    // Checks we have all arg inputs
                    if (args.Length != 6) return false;

                    return CreateFund((byte[])args[0], (string)args[1], (byte[])args[2], (byte[])args[3], (BigInteger)args[4], (BigInteger)args[5]);
                }

                // CONTRIBUTOR //
                // GET FUND PARAMETER: (fid, param)
                if (operation == "GetFundParameter") return GetFundParameter((string)args[0], (string)args[1]);

                // DEPOSIT FUNDS: (fid, asset, contributorSH) 
                if (operation == "DepositFunds") return DepositFunds((string)args[0], (byte[])args[1], (byte[])args[2]);
                //if (operation == "DepositFunds") return GetFundParameter((string)args[0], (string)args[1]);

                // REACHED GOAL QUERY: (fid)
                if (operation == "ReachedGoal") return ReachedGoal((string)args[0]);

                // REACHED END TIME QUERY: (fid)
                if (operation == "ReachedEndTime") return ReachedEndTime((string)args[0]);

                // IS REFUND ACTIVE: (fid)
                if (operation == "IsRefundActive") return IsRefundActive((string)args[0]);

                // CONTRIBUTOR INFO: (fid, GetContributorInfo, key)
                if (operation == "GetContributorInfo") return GetContributorInfo((string)args[0], (byte[])args[1], (string)args[2]);

                // GET FUNDS FROM CONTRIBUTOR: (GetContributorInfo)
                if (operation == "GetFundsFromContributorSH") return GetFundsFromContributorSH((byte[])args[0]);

                // SUBMIT WITHDRAW REQUEST
                if (operation == "WithdrawFundsRequest") return WithdrawFundsRequest((string)args[0], (byte[])args[1], (BigInteger)args[2]);
                
                // SUBMIT WITHDRAW REQUEST RESET
                if (operation == "WithdrawRequestReset") return WithdrawRequestReset((byte[])args[0]);

            }

            return false;
        }

        private static bool WithdrawFundsRequest(string fid, byte[] contributorSH, BigInteger requestedAmount)
        {
            if (requestedAmount <= 0) return false;

            // contributorSH Check
            if (!IsContributorSH(contributorSH)) return false;

            // Calculates total amount 
            BigInteger fundsOwed = CheckContributorOwed(fid, contributorSH);

            // If Requested is more than CheckContributorOwed() = FAIL
            if (requestedAmount > fundsOwed)
            {
                Runtime.Notify("WithdrawFundsRequest() => withdrawRequested too high, max owing is:", fundsOwed);
                return false;
            }
            
            // Gets current balance for contributorSH
            BigInteger bal = SubStorageGet(fid, contributorSH.AsString(), "balance").AsBigInteger();
            BigInteger owed = SubStorageGet(fid, contributorSH.AsString(), "owed").AsBigInteger();

            // Removes an even portion of each fund balance                            
            BigInteger newOwed = owed;

            // If withdraw Request can be filled
            if (requestedAmount <= owed)
            {
                newOwed = owed - requestedAmount;
            }

            // If withdraw Requested can only be partially filled
            else if (requestedAmount > owed)
            {
                newOwed = 0;
            }

            // Update fund balance
            BigInteger newBal = bal - (owed - newOwed);

            // Update contributorSH Storage details
            SubStoragePut(fid, contributorSH.AsString(), "balance", newBal.AsByteArray());
            SubStoragePut(fid, contributorSH.AsString(), "owed", newOwed.AsByteArray());    

            StoragePut(contributorSH.AsString(), "withdrawHold", requestedAmount.AsByteArray());

            Runtime.Notify("WithdrawFundsRequest() => Sucessfully sent withdraw request");
            return true;
        }

        private static bool WithdrawRequestReset(byte[] contributorSH)
        {
            // contributorSH Check
            if (!IsContributorSH(contributorSH)) return false;

            StoragePut(contributorSH.AsString(), "withdrawHold", new byte[0]);
            Runtime.Notify("Reset Withdraw Request!");

            return true;
        }


        private static bool CreateFund(byte[] creatorSH, string fid, byte[] asset, byte[] withdrawalSH, BigInteger goal, BigInteger endtime)
        {
            Runtime.Notify("CreatingFund: ", fid);

            // If creatorSH isnt actually the creator 
            if (!Runtime.CheckWitness(creatorSH)) return false;

            // If fund already exists with same fid, exit.
            if (FundExists(fid)) return false;

            // Saves fid to contract storage
            Storage.Put(Storage.CurrentContext, fid, fid);

            // Default Balance
            BigInteger newBalance = 0;

            // Saves defaults to storage            
            StoragePut(fid, "creatorSH", creatorSH);
            StoragePut(fid, "asset", asset);
            StoragePut(fid, "withdrawalSH", withdrawalSH);
            StoragePut(fid, "goal", goal.ToByteArray());
            StoragePut(fid, "endtime", endtime.ToByteArray());
            StoragePut(fid, "fundBalance", newBalance.ToByteArray());

            Runtime.Notify("CreateFund() => Successfully Created Fund!", fid);
            return true;
        }

        private static bool DepositFunds(string fid, byte[] asset, byte[] contributorSH)
        {
            Runtime.Notify("Depositing Funds to:", fid);

            // If fund exists.
            if (!FundExists(fid)) return false;
            Runtime.Notify("Found existing Fund", fid);

            // Gets the deposit amount 
            BigInteger txAmount = GetTransactionAmount(asset);
            Runtime.Notify("txAmount", txAmount);

            // Updates balance if deposited amount is bigger than 0
            if (txAmount > 0)
            {
                // Updates contributorSH details
                SaveContributorInfo(fid, asset, contributorSH);

                BigInteger newBalance = GetFundParameter(fid, "fundBalance").AsBigInteger() + txAmount;
                StoragePut(fid, "fundBalance", newBalance.ToByteArray());

                Runtime.Notify("Deposited funds to: ", fid, txAmount);
                return true;
            }

            Runtime.Notify("Failed to Deposited funds to: ", fid);
            return false;
        }

        // returns the the fund paramter
        private static byte[] GetFundParameter(string fid, string param)
        {
            Runtime.Notify("Getting Fund Param: ", fid, param);
            Runtime.Notify("Fund Param: ", StorageGet(fid, param));
            return StorageGet(fid, param);
        }

        // Querys to see if Fund has reached its goal
        private static bool ReachedGoal(string fid)
        {
            // Get stored values
            BigInteger goal = GetFundParameter(fid, "goal").AsBigInteger();
            BigInteger balance = GetFundParameter(fid, "fundBalance").AsBigInteger();

            // If Balance is higher than goal
            if (balance >= goal) return true;
            else return false;
        }

        // Querys to see if Fund has reached its goal
        private static bool ReachedEndTime(string fid)
        {
            // Get stored values
            BigInteger endtime = GetFundParameter(fid, "endtime").AsBigInteger();
            BigInteger currentTime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp + 15; // previousBlockTime + 15 sec
            
            // If Balance is higher than goal
            if (currentTime >= endtime) return true;
            else return false;
        }

        private static bool IsRefundActive(string fid)
        {
            // If the goal HAS NOT been met
            if (ReachedGoal(fid)) return false;

            // And If the end time HAS been met
            if (!ReachedEndTime(fid)) return false;

            // Then Refund process active (aka fund failed)
            return true;
        }



        // Gets the sender transaction object [AssetId, ScriptHash, Value]
        private static TransactionOutput[] GetSenderObjects()
        {
            // TODO
            // So we only call GetReferences() once
            //if (references == null)
            //{
            //    Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            //    TransactionOutput[] references = tx.GetReferences();
            //}

            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] outputs = tx.GetOutputs();
            return outputs;
        }

        private static TransactionOutput GetSenderObjectForAsset(TransactionOutput[] references, byte[] asset)
        {
            foreach (TransactionOutput reference in references)
            {
                //return reference;
                if (reference.AssetId == asset)
                {
                    // Only one transaction supported, returns the first                    
                    return reference;
                }
            }

            // Return Null object
            return new TransactionOutput();
        }

        private static TransactionOutput GetSenderObject(TransactionOutput[] references)
        {
            foreach (TransactionOutput reference in references)
            {
                return reference;
            }

            // Return Null object
            return new TransactionOutput();
        }

        // Gets the amount of assest depositied
        private static BigInteger GetTransactionAmount(byte[] asset)
        {
            // Gets the sender transaction object [AssetId, ScriptHash, Value]
            TransactionOutput senderObject = GetSenderObjectForAsset(GetSenderObjects(), asset);

            BigInteger actualAmount = (long)senderObject.Value / (long)100000000;

            // If the transaction asset matches return the amount
            if (senderObject.AssetId == asset) return actualAmount;

            return actualAmount;
        }

        // Saves funds accociated to contributorSH
        private static void AddFundToContributorSH(string fid, byte[] contributorSH)
        {
            // Gets the saved amount of funds accociated to contributorSH
            BigInteger numFunds = Storage.Get(Storage.CurrentContext, string.Concat(contributorSH, "numFunds")).AsBigInteger();

            // Saves the input fund to the contributorSH at the +1 numFunds index
            Storage.Put(Storage.CurrentContext, string.Concat(contributorSH, "numFunds"), numFunds + 1);
            Storage.Put(Storage.CurrentContext, string.Concat(contributorSH, (numFunds + 1)), fid);
        }

        // Gets funds accociated to contributorSH
        private static string[] GetFundsFromContributorSH(byte[] contributorSH)
        {
            // Gets the saved amount of funds accociated to contributorSH
            int numFunds = (int)Storage.Get(Storage.CurrentContext, string.Concat(contributorSH, "numFunds")).AsBigInteger();

            // Empty byte array array at length of numFunds
            string[] funds = new string[numFunds];

            // Loop through each fund index and Get from storage
            for (int i = 0; i < numFunds; i++)
            {
                string fid = Storage.Get(Storage.CurrentContext, string.Concat(contributorSH, i+1)).AsString();
                funds[i] = fid;
            }

            // Returns funds byte array array
            return funds;
        }


        // TODO - add email
        // Will add contributor Address details to Storage.
        private static bool SaveContributorInfo(string fid, byte[] asset, byte[] contributorSH)
        {
            BigInteger bal;
            BigInteger owed = 0;
 
            // If contributorSH storage doesnt exist, adds it.
            if (SubStorageGet(fid, "contributorSH", contributorSH.AsString()) == null)
            {
                // Saving contributorSH to fund
                SubStoragePut(fid, "contributorSH", contributorSH.AsString(), contributorSH);
                bal = GetTransactionAmount(asset);
                AddFundToContributorSH(fid, contributorSH);
            }

            // If contributorSH already exists
            else
            {
                bal = GetTransactionAmount(asset) + GetContributorInfo(fid, contributorSH, "balance").AsBigInteger();
            }

            // If Funding has faild and the refund is active, entire balance is set to owed
            if (IsRefundActive(fid)) owed = bal;

            // Update contributorSH Storage details
            SubStoragePut(fid, contributorSH.AsString(), "balance", bal.AsByteArray());
            SubStoragePut(fid, contributorSH.AsString(), "owed", owed.AsByteArray());

            return true;
        }

        private static BigInteger CheckContributorOwed(string fid, byte[] contributorSH)
        {
            BigInteger bal = GetContributorInfo(fid, contributorSH, "balance").AsBigInteger();
            BigInteger owed = 0;

            // If Funding has faild and the refund is active, entire balance is set to owed
            if (IsRefundActive(fid)) owed = bal;

            // Update contributorSH Storage details
            SubStoragePut(fid, contributorSH.AsString(), "owed", owed.AsByteArray());

            return owed;
        }

        // Gets params of Contributor Address within the specified fund
        private static byte[] GetContributorInfo(string fid, byte[] contributorSH, string key)
        {
            Runtime.Notify("Getting ContributorInfo: ", fid, contributorSH, key);
            return SubStorageGet(fid, contributorSH.AsString(), key);
        }

        // Saves value to storage using unique id and key
        private static void StoragePut(string fid, string key, byte[] value)
        {
            Runtime.Notify("StoragePut", string.Concat(fid, key), value);
            Storage.Put(Storage.CurrentContext, string.Concat(fid, key), value);
        }

        // Saves value to storage using unique id and key and sub key
        private static void SubStoragePut(string fid, string key, string subKey, byte[] value)
        {
            string storageKey = string.Concat(string.Concat(fid, key), subKey);
            Runtime.Notify("SubStoragePut storageKey", storageKey);
            Storage.Put(Storage.CurrentContext, storageKey, value);
        }

        // Gets value from storage using unique id and key
        private static byte[] StorageGet(string fid, string key)
        {
            Runtime.Notify("StorageGet", string.Concat(fid, key));
            return Storage.Get(Storage.CurrentContext, string.Concat(fid, key));
        }

        // Gets value from storage using unique id and key sub key
        private static byte[] SubStorageGet(string fid, string key, string subKey)
        {
            string storageKey = string.Concat(string.Concat(fid, key), subKey);
            Runtime.Notify("SubStorageGet storageKey", storageKey);
            return Storage.Get(Storage.CurrentContext, storageKey);
        }

        // Checks storage for exisiting fid, returns false if null
        private static bool FundExists(string fid)
        {
            Runtime.Notify("Storage.Get(fid)", Storage.Get(Storage.CurrentContext, fid));
            if (Storage.Get(Storage.CurrentContext, fid) == null) return false;
            else return true;
        }

        // Sets the contract fee, only by the checked admin
        private static bool SetFee(byte[] sender, BigInteger fee)
        {
            Runtime.Notify("Setting Fee: ", fee, sender);

            // if Admin
            if (!IsAdminSH(sender)) return false;

            Storage.Put(Storage.CurrentContext, "Fee", fee);

            Runtime.Notify("Fee Set: ", fee);
            return true;
        }

        // Checks input sender script hash if its the contract admin, and if its a Checked Witness.
        private static bool IsAdminSH(byte[] sender)
        {
            // If sender is script hash
            if (sender.Length == 20)
            {
                // If input sender is admin, and if sender is verified as true
                if (sender == admin) return Runtime.CheckWitness(sender);
            }
            return false;
        }

        // Checks input sender script hash if its the fid's creator, and if its a Checked Witness.
        private static bool IsCreatorSH(byte[] senderSH, string fid)
        {
            // If sender is script hash
            if (senderSH.Length == 20)
            {
                // Gets the verified creatorSH from storage
                byte[] creatorSH = GetFundParameter(fid, "creatorSH");

                // If input sender and creator match, and CheckWitness is true
                if (creatorSH == senderSH) return Runtime.CheckWitness(senderSH);
            }
            return false;
        }

        // Checks input sender script hash, and if its a Checked Witness.
        private static bool IsContributorSH(byte[] ContributorSH)
        {
            // If sender is script hash
            if (ContributorSH.Length == 20) return Runtime.CheckWitness(ContributorSH);

            Runtime.Notify("IsContributorSH() => contributorSH is not Vaild", ContributorSH);
            return false;
        }

    }
}
