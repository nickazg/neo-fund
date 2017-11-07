using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace NeoFund
{
    public class NeoFundContract : Neo.SmartContract.Framework.SmartContract
    {
        private static readonly byte[] neo = { 197, 111, 51, 252, 110, 207, 205, 12, 34, 92, 74, 179, 86, 254, 229, 147, 144, 175, 133, 96, 190, 147, 15, 174, 190, 116, 166, 218, 255, 124, 155 };
        private static readonly byte[] admin = { 0 };
        public static TransactionOutput[] references;

        //private static readonly int fidLength = 20;
        //private static readonly int assetIDLength = 32;
        //private static readonly int contributorSHLength = 33;
        // = 85


        public static Object Main(string operation, params object[] args)
        {
            Runtime.Notify("Version 1.18");
            Runtime.Notify(Runtime.Trigger);
            Runtime.Notify("operation", operation);
            Runtime.Notify("arg length ", args.Length);
            Runtime.Notify("args", args);

            // Contract transaction, ie assest deposit/withdrawl transaction (operation == signature)
            if (Runtime.Trigger == TriggerType.Verification)
            {
                // Block Deposits that arent correctly invoked.

                // Refund Transaction (Allow if goal isnt reached)
                //TransactionOutput senderObj = GetSenderObject(neo);
                //byte[] senderSH = (byte[])senderObj.ScriptHash;

                //if (IsContributorSH(senderSH))
                //{
                //    // TODO #
                //    BigInteger amountRequested = (BigInteger)senderObj.Value;

                //    // if Requested is less than GetTotalFundsOwed()
                //    BigInteger totalOwed = GetTotalFundsOwed(senderSH);
                //    if (amountRequested <= totalOwed)
                //    {
                //        BigInteger removalRaito = totalOwed / amountRequested;

                //        // Updates balances for senderSH on each fund (even portion removed)
                //        byte[][] contributedFunds = GetFundsFromContributorSH(senderSH);
                //        for (int i = 0; i < contributedFunds.Length; i++)
                //        {
                //            string fid = contributedFunds[i].AsString();

                //            // Gets current balance for senderSH
                //            BigInteger bal = SubStorageGet(fid, senderSH.AsString(), "balance").AsBigInteger();
                //            BigInteger owed = SubStorageGet(fid, senderSH.AsString(), "owed").AsBigInteger();

                //            // Removes an even portion of each fund balance                            
                //            BigInteger newOwed = owed / removalRaito;
                //            BigInteger newBal = bal - (owed - newOwed);

                //            // Update contributorSH Storage details
                //            SubStoragePut(fid, senderSH.AsString(), "balance", newBal.AsByteArray());
                //            SubStoragePut(fid, senderSH.AsString(), "owed", newOwed.AsByteArray());
                //        }

                //        return true;
                //    }

                //    return false;

                //}

                // Withdrawl Transaction to goal address (Allow if goal is reached)
            }

            // Invocation transaction
            else if (Runtime.Trigger == TriggerType.Application)
            {
                Runtime.Notify("Runtime.Trigger");
                // Operation Permissions:
                //      Admin:          SetFee
                //      Creator:        CreateFund, DeleteFund
                //      Contributor:    DepositFunds, GetFundParameter, ReachedGoal, ReachedEndTime, IsRefundActive, GetContributorInfo, GetTotalFundsOwed 
                //  
                //      Todo: Donation/Reward Tiers, GetNumContributors 

                // ADMIN // 
                // TODO - Does nothing
                // SET FEE 
                if (operation == "SetFee") return SetFee((byte[])args[0], (BigInteger)args[1]);

                // CREATOR //
                // CREATE FUND
                if (operation == "CreateFund")
                {
                    Runtime.Notify("CreateFund ");
                    // Checks we have all arg inputs
                    if (args.Length != 6) return false;

                    Runtime.Notify("Checking Params");
                    // assigns correct types to input args
                    byte[] creatorSH = (byte[])args[0];
                    Runtime.Notify("creatorSH", creatorSH);
                    string fid = (string)args[1];
                    Runtime.Notify("fid", fid);
                    byte[] asset = (byte[])args[2];
                    Runtime.Notify("asset", asset);
                    byte[] withdrawalSH = (byte[])args[3];
                    Runtime.Notify("withdrawalSH", withdrawalSH);
                    BigInteger goal = (BigInteger)args[4];
                    Runtime.Notify("goal", (int)args[4]);
                    BigInteger endtime = (BigInteger)args[5];
                    Runtime.Notify("endtime", (int)args[5]);

                    Runtime.Notify("Executing Params");
                    // execute CreateFund()
                    return CreateFund(creatorSH, fid, asset, withdrawalSH, goal, endtime);
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

                // GET TOTAL FUNDS OWED TO CONTRIBUTOR: (GetContributorInfo)
                if (operation == "GetTotalFundsOwed") return GetTotalFundsOwed((byte[])args[0]);

            }

            return false;
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

            Runtime.Notify("Successfully Created Fund!", fid);
            return true;
        }

        private static bool DepositFunds(string fid, byte[] asset, byte[] contributorSH)
        {
            Runtime.Notify("Depositing Funds to:", fid);

            // If fund exists.
            if (!FundExists(fid)) return false;
            Runtime.Notify("Fund Exists!", fid);

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
            Runtime.Notify("Fund Param: ", StorageGet(fid, param).AsBigInteger());
            return StorageGet(fid, param);
        }

        // Querys to see if Fund has reached its goal
        private static bool ReachedGoal(string fid)
        {
            Runtime.Notify("Checking Goal: ", fid);

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
            Runtime.Notify("Checking End Time: ", fid);

            // Get stored values
            BigInteger endtime = GetFundParameter(fid, "endtime").AsBigInteger();
            BigInteger currentTime = Blockchain.GetHeader(Blockchain.GetHeight()).Timestamp + 15; // previousBlockTime + 15 sec

            // If Balance is higher than goal
            if (currentTime >= endtime) return true;
            else return false;
        }

        private static bool IsRefundActive(string fid)
        {
            Runtime.Notify("Checking refund Status: ", fid);

            // If the goal hasnt been met
            if (ReachedGoal(fid)) return false;

            // And If the end time also hasnt been met
            if (ReachedEndTime(fid)) return true;

            // Then Refund not active
            return false;
        }

        private static BigInteger GetTotalFundsOwed(byte[] contributorSH)
        {
            Runtime.Notify("Getting Total Funds Owed: ", contributorSH);

            BigInteger totalOwed = 0;

            byte[][] contributedFunds = GetFundsFromContributorSH(contributorSH);

            for (int i = 0; i < contributedFunds.Length; i++)
            {
                string fid = contributedFunds[i].AsString();
                totalOwed += GetContributorInfo(fid, contributorSH, "owed").AsBigInteger();
            }

            Runtime.Notify("Total Funds Owed: ", totalOwed);
            return totalOwed;
        }

        // TODO, Need to verify this.
        // Gets the sender transaction object [AssetId, ScriptHash, Value]
        private static TransactionOutput GetSenderObject(byte[] asset)
        {
            // So we only call GetReferences() once
            //if (references == null)
            //{
            //    Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            //    TransactionOutput[] references = tx.GetReferences();
            //    Runtime.Notify("Transaction", tx);
            //    Runtime.Notify("Transaction Hash", tx.Hash);
            //    Runtime.Notify("references", references);
            //}

            Transaction tx = (Transaction)ExecutionEngine.ScriptContainer;
            TransactionOutput[] references = tx.GetOutputs();

            foreach (TransactionOutput reference in references)
            {
                //return reference;
                if (reference.AssetId == asset)
                {
                    Runtime.Notify("reference.AssetId == asset");
                    // Only one transaction supported, returns the first                    
                    return reference;
                }
            }

            // Return Null object
            return new TransactionOutput();
        }

        // Gets the amount of assest depositied
        private static BigInteger GetTransactionAmount(byte[] asset)
        {
            // Gets the sender transaction object [AssetId, ScriptHash, Value]
            TransactionOutput senderObject = GetSenderObject(asset);

            BigInteger actualAmount = (long)senderObject.Value / (long)100000000;

            //// If the transaction asset matches return the amount
            if (senderObject.AssetId == asset) return actualAmount;

            return actualAmount;
        }

        // Saves funds accociated to contributorSH
        private static void AddFundToContributorSH(byte[] contributorSH, string fid)
        {
            // Gets the saved amount of funds accociated to contributorSH
            BigInteger numFunds = Storage.Get(Storage.CurrentContext, string.Concat(contributorSH, "numFunds")).AsBigInteger();

            // Saves the input fund to the contributorSH at the +1 numFunds index
            Storage.Put(Storage.CurrentContext, string.Concat(contributorSH, (numFunds + 1)), fid);
        }

        // Gets funds accociated to contributorSH
        private static byte[][] GetFundsFromContributorSH(byte[] contributorSH)
        {
            // Gets the saved amount of funds accociated to contributorSH
            int numFunds = (int)Storage.Get(Storage.CurrentContext, string.Concat(contributorSH, "numFunds")).AsBigInteger();

            // Empty byte array array at length of numFunds
            byte[][] funds = new byte[numFunds][];

            // Loop through each fund index and Get from storage
            for (int i = 0; i < numFunds; i++)
            {
                string fid = Storage.Get(Storage.CurrentContext, string.Concat(contributorSH, i)).AsString();
                funds[i] = fid.AsByteArray();
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
            Storage.Put(Storage.CurrentContext, string.Concat(fid, key, subKey), value);
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
            return Storage.Get(Storage.CurrentContext, string.Concat(fid, key, subKey));
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
        private static bool IsCreatorSH(byte[] sender, string fid)
        {
            // If sender is script hash
            if (sender.Length == 20)
            {
                // Gets the verified creatorSH from storage
                byte[] creatorSH = GetFundParameter(fid, "creatorSH");

                // If input sender is admin, and if sender is verified as true
                if (creatorSH == admin) return Runtime.CheckWitness(creatorSH);
            }
            return false;
        }

        // Checks input sender script hash, and if its a Checked Witness.
        private static bool IsContributorSH(byte[] sender)
        {
            // If sender is script hash
            if (sender.Length == 20) return Runtime.CheckWitness(sender);
            return false;
        }

    }
}
